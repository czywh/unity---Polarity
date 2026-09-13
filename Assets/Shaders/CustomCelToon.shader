Shader "Custom/URP/CustomCelToonAA"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)

        // Outline
        _OutlineColor(
            "Outline Color",
            Color
        ) = (0,0,0,1)

        _OutlineWidthPx(
            "Outline Width (Pixels)",
            Range(0,3)
        ) = 0.35

        // Diffuse toon ramp
        _Steps(
            "Light Steps",
            Range(1,5)
        ) = 3

        _Feather(
            "Base Feather",
            Range(0,0.5)
        ) = 0.02

        _CelAAStrength(
            "Cel Anti-Flicker",
            Range(0,4)
        ) = 1.5

        // Specular
        _SpecColor(
            "Spec Color",
            Color
        ) = (1,1,1,1)

        _SpecStrength(
            "Spec Strength",
            Range(0,2)
        ) = 0.5

        _SpecThreshold(
            "Spec Threshold",
            Range(0,1)
        ) = 0.5

        _SpecFeather(
            "Spec Feather",
            Range(0,0.5)
        ) = 0.03

        _SpecAAStrength(
            "Spec Anti-Flicker",
            Range(0,4)
        ) = 1.0

        _Gloss(
            "Glossiness",
            Range(8,256)
        ) = 64

        // Rim lighting
        _RimColor(
            "Rim Color",
            Color
        ) = (1,1,1,1)

        _RimPower(
            "Rim Power",
            Range(0.5,8)
        ) = 2

        _RimThreshold(
            "Rim Threshold",
            Range(0,1)
        ) = 0.35

        _RimFeather(
            "Rim Feather",
            Range(0,0.5)
        ) = 0.03

        _RimAAStrength(
            "Rim Anti-Flicker",
            Range(0,4)
        ) = 1.5

        // Shadow parameters
        _ShadowFadeStart(
            "Shadow Fade Start",
            Range(0,200)
        ) = 35

        _ShadowFadeEnd(
            "Shadow Fade End",
            Range(0,300)
        ) = 50

        _MinShadow(
            "Min Shadow Brightness",
            Range(0,0.6)
        ) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        LOD 300

        // ============================================================
        // Pass 1: Outline
        // ============================================================

        Pass
        {
            Name "OUTLINE"

            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
            }

            Cull Front
            ZWrite On
            ZTest LEqual
            ColorMask RGB

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag

            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;

                float4 _OutlineColor;
                float _OutlineWidthPx;

                float _Steps;
                float _Feather;
                float _CelAAStrength;

                float4 _SpecColor;
                float _SpecStrength;
                float _SpecThreshold;
                float _SpecFeather;
                float _SpecAAStrength;
                float _Gloss;

                float4 _RimColor;
                float _RimPower;
                float _RimThreshold;
                float _RimFeather;
                float _RimAAStrength;

                float _ShadowFadeStart;
                float _ShadowFadeEnd;
                float _MinShadow;
            CBUFFER_END

            struct OutlineAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct OutlineVaryings
            {
                float4 positionHCS : SV_POSITION;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            OutlineVaryings OutlineVert(
                OutlineAttributes input
            )
            {
                OutlineVaryings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float4 clipPosition =
                    TransformObjectToHClip(
                        input.positionOS.xyz
                    );

                /*
                 * 将法线转换到观察空间。
                 * XY 方向作为屏幕空间描边挤出方向。
                 */
                float3 normalVS =
                    mul(
                        (float3x3)UNITY_MATRIX_IT_MV,
                        input.normalOS
                    );

                normalVS.z = 0.0;

                float2 normalXY =
                    normalVS.xy;

                float normalLength =
                    max(
                        length(normalXY),
                        0.00001
                    );

                float2 direction =
                    normalXY / normalLength;

                /*
                 * NDC 中一个像素的尺寸。
                 * 使用屏幕高度控制描边宽度。
                 */
                float pixelSizeNDC =
                    2.0 / _ScreenParams.y;

                float2 offsetNDC =
                    direction
                    * _OutlineWidthPx
                    * pixelSizeNDC;

                clipPosition.xy +=
                    offsetNDC
                    * clipPosition.w;

                output.positionHCS =
                    clipPosition;

                return output;
            }

            half4 OutlineFrag(
                OutlineVaryings input
            ) : SV_Target
            {
                return half4(
                    _OutlineColor.rgb,
                    1.0h
                );
            }

            ENDHLSL
        }

        // ============================================================
        // Pass 2: Toon Forward
        // ============================================================

        Pass
        {
            Name "UniversalForward"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex ToonVert
            #pragma fragment ToonFrag

            #pragma multi_compile_instancing

            // Main light shadows
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            // Fog
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;

                float4 _OutlineColor;
                float _OutlineWidthPx;

                float _Steps;
                float _Feather;
                float _CelAAStrength;

                float4 _SpecColor;
                float _SpecStrength;
                float _SpecThreshold;
                float _SpecFeather;
                float _SpecAAStrength;
                float _Gloss;

                float4 _RimColor;
                float _RimPower;
                float _RimThreshold;
                float _RimFeather;
                float _RimAAStrength;

                float _ShadowFadeStart;
                float _ShadowFadeEnd;
                float _MinShadow;
            CBUFFER_END

            struct ToonAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ToonVaryings
            {
                float4 positionHCS : SV_POSITION;

                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;

                half fogFactor : TEXCOORD4;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            ToonVaryings ToonVert(
                ToonAttributes input
            )
            {
                ToonVaryings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(
                        input.positionOS.xyz
                    );

                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(
                        input.normalOS
                    );

                output.positionHCS =
                    positionInputs.positionCS;

                output.positionWS =
                    positionInputs.positionWS;

                output.normalWS =
                    normalInputs.normalWS;

                output.uv =
                    TRANSFORM_TEX(
                        input.uv,
                        _BaseMap
                    );

                output.shadowCoord =
                    GetShadowCoord(
                        positionInputs
                    );

                output.fogFactor =
                    ComputeFogFactor(
                        positionInputs.positionCS.z
                    );

                return output;
            }

            // ========================================================
            // Anti-aliased toon ramp
            // ========================================================

            half CelIntensityAA(
                half value
            )
            {
                value = saturate(value);

                int stepCount =
                    clamp(
                        (int)round(_Steps),
                        1,
                        5
                    );

                /*
                 * 只有一级时返回连续光照。
                 * 避免所有像素都落到同一个黑色阶梯。
                 */
                if (stepCount <= 1)
                {
                    return value;
                }

                half intervalCount =
                    (half)(stepCount - 1);

                /*
                 * 当前光照值在相邻像素间的变化量。
                 * 变化越大，自动过渡宽度越大。
                 */
                half derivativeWidth =
                    fwidth(value)
                    * (half)_CelAAStrength;

                half transitionWidth =
                    max(
                        (half)_Feather,
                        derivativeWidth
                    );

                transitionWidth =
                    max(
                        transitionWidth,
                        0.0001h
                    );

                half result = 0.0h;

                /*
                 * 最多五个亮度等级，
                 * 因此最多四条分界线。
                 */
                [unroll]
                for (
                    int level = 0;
                    level < 4;
                    level++
                )
                {
                    if (
                        level >= stepCount - 1
                    )
                    {
                        break;
                    }

                    /*
                     * 分界线放在相邻亮度等级中间。
                     *
                     * steps = 3 时：
                     * 输出 0、0.5、1
                     * 阈值 0.25、0.75
                     */
                    half threshold =
                        (
                            (half)level
                            + 0.5h
                        )
                        / intervalCount;

                    half band =
                        smoothstep(
                            threshold
                                - transitionWidth,
                            threshold
                                + transitionWidth,
                            value
                        );

                    result +=
                        band / intervalCount;
                }

                return saturate(result);
            }

            // ========================================================
            // Main light shading
            // ========================================================

            half3 ShadeMainLight(
                half3 normalWS,
                half3 viewDirectionWS,
                float3 positionWS,
                Light light,
                half3 baseMapColor,
                out half3 specularColor
            )
            {
                normalWS =
                    SafeNormalize(normalWS);

                viewDirectionWS =
                    SafeNormalize(viewDirectionWS);

                half ndl =
                    saturate(
                        dot(
                            normalWS,
                            light.direction
                        )
                    );

                /*
                 * 阴影和距离衰减必须分开。
                 * 阴影不能在 toonInput 和最终颜色中重复相乘。
                 */
                half shadowAttenuation =
                    saturate(
                        light.shadowAttenuation
                    );

                half distanceAttenuation =
                    light.distanceAttenuation;

                /*
                 * 阴影区域保留最低亮度。
                 */
                half nearShadow =
                    lerp(
                        (half)_MinShadow,
                        1.0h,
                        shadowAttenuation
                    );

                float cameraDistance =
                    distance(
                        _WorldSpaceCameraPos,
                        positionWS
                    );

                float fadeRange =
                    max(
                        _ShadowFadeEnd
                            - _ShadowFadeStart,
                        0.001
                    );

                /*
                 * 近处为 1，远处为 0。
                 */
                half shadowDistanceFade =
                    saturate(
                        (
                            _ShadowFadeEnd
                            - cameraDistance
                        )
                        / fadeRange
                    );

                /*
                 * 近处使用实时阴影，
                 * 远处逐渐恢复为完全受光。
                 */
                half fadedShadow =
                    lerp(
                        1.0h,
                        nearShadow,
                        shadowDistanceFade
                    );

                half toonInput =
                    ndl * fadedShadow;

                half toonLighting =
                    CelIntensityAA(
                        toonInput
                    );

                half3 diffuseColor =
                    baseMapColor
                    * toonLighting
                    * light.color
                    * distanceAttenuation;

                // ====================================================
                // Anti-aliased Blinn-Phong specular
                // ====================================================

                half3 halfDirection =
                    SafeNormalize(
                        light.direction
                        + viewDirectionWS
                    );

                half ndh =
                    saturate(
                        dot(
                            normalWS,
                            halfDirection
                        )
                    );

                half rawSpecular =
                    pow(
                        ndh,
                        max(
                            (half)_Gloss,
                            1.0h
                        )
                    );

                half specDerivative =
                    fwidth(rawSpecular)
                    * (half)_SpecAAStrength;

                half specWidth =
                    max(
                        (half)_SpecFeather,
                        specDerivative
                    );

                specWidth =
                    max(
                        specWidth,
                        0.0001h
                    );

                half specularMask =
                    smoothstep(
                        (half)_SpecThreshold
                            - specWidth,
                        (half)_SpecThreshold
                            + specWidth,
                        rawSpecular
                    );

                /*
                 * 背光面关闭高光。
                 * 不使用硬 step，避免跨阈值闪烁。
                 */
                half ndlWidth =
                    max(
                        fwidth(ndl),
                        0.001h
                    );

                half frontFaceMask =
                    smoothstep(
                        0.0h,
                        ndlWidth,
                        ndl
                    );

                specularMask *=
                    frontFaceMask;

                specularMask *=
                    fadedShadow;

                specularColor =
                    specularMask
                    * (half)_SpecStrength
                    * _SpecColor.rgb
                    * light.color
                    * distanceAttenuation;

                return diffuseColor;
            }

            // ========================================================
            // Fragment
            // ========================================================

            half4 ToonFrag(
                ToonVaryings input
            ) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 normalWS =
                    SafeNormalize(
                        input.normalWS
                    );

                half3 viewDirectionWS =
                    SafeNormalize(
                        GetWorldSpaceViewDir(
                            input.positionWS
                        )
                    );

                half4 baseSample =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        input.uv
                    );

                half3 baseMapColor =
                    baseSample.rgb
                    * _BaseColor.rgb;

                /*
                 * 必须传入 shadowCoord，
                 * 才能正确读取主光源实时阴影。
                 */
                Light mainLight =
                    GetMainLight(
                        input.shadowCoord
                    );

                half3 specularColor;

                half3 diffuseColor =
                    ShadeMainLight(
                        normalWS,
                        viewDirectionWS,
                        input.positionWS,
                        mainLight,
                        baseMapColor,
                        specularColor
                    );

                // Environment lighting
                half3 ambientColor =
                    SampleSH(normalWS)
                    * baseMapColor;

                // ====================================================
                // Anti-aliased Fresnel / Rim
                // ====================================================

                half ndv =
                    saturate(
                        dot(
                            normalWS,
                            viewDirectionWS
                        )
                    );

                half fresnel =
                    pow(
                        saturate(
                            1.0h - ndv
                        ),
                        max(
                            (half)_RimPower,
                            0.001h
                        )
                    );

                half rimDerivative =
                    fwidth(fresnel)
                    * (half)_RimAAStrength;

                half rimWidth =
                    max(
                        (half)_RimFeather,
                        rimDerivative
                    );

                rimWidth =
                    max(
                        rimWidth,
                        0.0001h
                    );

                half rimFactor =
                    smoothstep(
                        (half)_RimThreshold
                            - rimWidth,
                        (half)_RimThreshold
                            + rimWidth,
                        fresnel
                    );

                half3 rimColor =
                    _RimColor.rgb
                    * rimFactor;

                half3 finalColor =
                    ambientColor
                    + diffuseColor
                    + specularColor
                    + rimColor;

                finalColor =
                    MixFog(
                        finalColor,
                        input.fogFactor
                    );

                return half4(
                    finalColor,
                    baseSample.a
                        * _BaseColor.a
                );
            }

            ENDHLSL
        }

        // ============================================================
        // URP standard passes
        // ============================================================

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    FallBack Off
}