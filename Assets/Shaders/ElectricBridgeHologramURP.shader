// ============================================================================
// ElectricBridgeHologram_URP.shader
// 电子桥充电全息材质（Universal Render Pipeline / URP）
//
// 由脚本 ElectricEntity 每帧写入 _Charge(0~1) 驱动全部动态表现：
//   · 透明度：_Charge=0 → 40% 不透明（灰色幽灵态）；_Charge=1 → 100%（实体紫）
//   · 扫描线 / 边缘光 / 发光：亮度随 _Charge 线性增强，越充满越"实"
//   · 充电液面：一条发光横线随电量从桥底升到桥顶，直观显示百分比
//   · 未充满时轻微闪烁（供电不稳），充满后稳定
//
// 颜色分工：
//   _BaseColor ← 脚本用 MaterialPropertyBlock 写入的"灰→紫"插值色（表面基色）
//   本 shader 独占透明度控制，所以颜色的 A 通道不再影响渲染。
//
// 注：用 MaterialPropertyBlock 逐物体改属性会让该物体退出 SRP Batcher（功能不受影响）。
// ============================================================================
Shader "Custom/ElectricBridgeHologramURP"
{
    Properties
    {
        [Header(Base)]
        _BaseColor    ("Tint 基色（脚本每帧写入 灰→紫）", Color) = (0.6, 0.2, 0.9, 1)
        _BaseMap      ("Base Texture 可选贴图", 2D) = "white" {}
        _Charge       ("Charge 电量（脚本写入 0~1）", Range(0,1)) = 1

        [Header(Transparency)]
        _MinAlpha     ("Min Alpha 未充电最低不透明度", Range(0,1)) = 0.4
        _MaxAlpha     ("Max Alpha 充满不透明度",       Range(0,1)) = 1.0

        [Header(Hologram Glow)]
        [HDR] _EmissionColor ("Emission 扫描/边缘发光色(HDR)", Color) = (0.75, 0.35, 1.4, 1)
        _RimPower     ("Fresnel Power 边缘锐度", Range(0.5, 8)) = 2.5
        _RimStrength  ("Fresnel Strength 边缘强度", Range(0, 4)) = 1.5

        [Header(Scanlines)]
        _ScanFrequency ("Scan Frequency 密度", Float) = 40
        _ScanSpeed     ("Scan Speed 上升速度", Float) = 2
        _ScanThickness ("Scan Thickness 线宽", Range(0,1)) = 0.15
        _ScanStrength  ("Scan Strength 强度", Range(0,3)) = 1

        [Header(Flicker)]
        _FlickerStrength ("Flicker Strength 低电量闪烁强度", Range(0,1)) = 0.3
        _FlickerSpeed    ("Flicker Speed 闪烁速度", Float) = 18

        [Header(Charge Fill)]
        [Toggle(_FILL_ON)] _FillFeature ("Enable 启用上升液面", Float) = 1
        [HDR] _FillColor  ("Fill Color 液面发光色(HDR)", Color) = (1.2, 0.6, 2.0, 1)
        _FillBandWidth ("Fill Band Width 液面亮线宽度", Range(0.001, 0.5)) = 0.05
        _FillMinY      ("Fill Min Y 物体空间底部Y", Float) = -0.5
        _FillMaxY      ("Fill Max Y 物体空间顶部Y", Float) = 0.5
        _FillSoftness  ("Fill Softness 已充区提亮", Range(0,2)) = 0.6

        [Header(Render)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode 剔除", Float) = 0 // 0=Off(双面全息) 2=Back
    }

    SubShader
    {
        Tags
        {
            "RenderType"       = "Transparent"
            "Queue"            = "Transparent"
            "RenderPipeline"   = "UniversalPipeline"
            "IgnoreProjector"  = "True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _FILL_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // SRP Batcher 要求：所有非贴图材质属性都放进 UnityPerMaterial
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float  _Charge;
                float  _MinAlpha;
                float  _MaxAlpha;
                float4 _EmissionColor;
                float  _RimPower;
                float  _RimStrength;
                float  _ScanFrequency;
                float  _ScanSpeed;
                float  _ScanThickness;
                float  _ScanStrength;
                float  _FlickerStrength;
                float  _FlickerSpeed;
                float4 _FillColor;
                float  _FillBandWidth;
                float  _FillMinY;
                float  _FillMaxY;
                float  _FillSoftness;
                float  _Cull;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float  objY       : TEXCOORD3; // 物体空间 Y，用于充电液面归一化
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.objY       = IN.positionOS.y; // 不受 transform 缩放影响，天然 -0.5~0.5（单位立方体）
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float charge = saturate(_Charge);

                // —— 表面基色：脚本每帧写入的灰→紫（× 可选贴图）——
                half3 baseCol = _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;

                // —— 边缘光 Fresnel ——
                float3 N   = normalize(IN.normalWS);
                float3 V   = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float  ndv = saturate(dot(N, V));
                float  rim = pow(1.0 - ndv, _RimPower) * _RimStrength;

                // —— 扫描线：世界 Y 方向的横向亮线，随时间上升 ——
                float scanCoord = IN.positionWS.y * _ScanFrequency - _Time.y * _ScanSpeed;
                float saw  = abs(frac(scanCoord) * 2.0 - 1.0);              // 三角波 0→1→0
                float scan = smoothstep(1.0 - _ScanThickness, 1.0, saw) * _ScanStrength;

                // —— 闪烁：电量越低越不稳，充满后趋于稳定 ——
                float flick = 1.0 - (1.0 - charge) * _FlickerStrength *
                              (0.5 + 0.5 * sin(_Time.y * _FlickerSpeed + IN.positionWS.y * 6.0));
                flick = saturate(flick);

                // —— 全息发光：扫描线 + 边缘光，强度随电量增长 ——
                half3 emission = _EmissionColor.rgb * (scan + rim) * charge * flick;

                // —— 充电液面：随 _Charge 从底升到顶，可视化百分比 ——
                float fillAlpha = 0.0;
            #ifdef _FILL_ON
                float t     = saturate((IN.objY - _FillMinY) / max(1e-4, (_FillMaxY - _FillMinY)));
                float band  = smoothstep(_FillBandWidth, 0.0, abs(t - charge));       // 液面处的亮线
                float below = smoothstep(charge + 0.02, charge - 0.02, t) * _FillSoftness; // 已充满的下半部分
                emission   += _FillColor.rgb * (band + below * 0.4);
                fillAlpha   = band * 0.5 + below * 0.15;
            #endif

                // —— 透明度：40% → 100%，再叠加边缘/扫描/液面的可见度 ——
                float baseA = lerp(_MinAlpha, _MaxAlpha, charge);
                float alpha = saturate(baseA + rim * 0.3 + scan * 0.15 + fillAlpha) * flick;

                return half4(baseCol + emission, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
