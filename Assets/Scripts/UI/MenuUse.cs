using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuUse : MonoBehaviour
{
    public string sceneName;
    public GameObject exitMenu;
    bool isExit;
    public GameObject blackCanvas;

    // Start is called before the first frame update
    void Start()
    {
        isExit = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (!isExit)
            {
                exitMenu.SetActive(true);
                isExit = true;
            }
            else
            {
                exitMenu.SetActive(false);
                isExit = false;
            }
        }
    }

    public void ChangeSceneByName()
    {
        Time.timeScale = 1;
        StartCoroutine(StartChangeCoroutine());
    }

    IEnumerator StartChangeCoroutine()
    {
        blackCanvas.SetActive(true);
        yield return new WaitForSeconds(2);
        SceneManager.LoadScene(sceneName);
    }

    public void QuitGame()
    {
        Application.Quit();
    }

    public void HideExitMenu()
    {
        exitMenu.SetActive(false);
        isExit = false;
    }
    public void ShowExitMenu()
    {
        exitMenu.SetActive(true);
        isExit = true;
    }
}
