using UnityEngine;
using UnityEngine.UI;

public class StartGame : MonoBehaviour
{
    public GameObject startScreen;
    public GameObject sceneUI;

    void Start()
    {
        startScreen.SetActive(true);  // Show the start screen initially
        sceneUI.SetActive(false);     // Hide the main scene UI
    }

    public void OnStartButtonPressed()
    {
        startScreen.SetActive(false);  // Hide the start screen
        sceneUI.SetActive(true);       // Show the scene UI
        // Add any additional logic if needed here
    }
}
