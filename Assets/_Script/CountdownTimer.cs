using System.Collections;
using UnityEngine;
using TMPro;

public class CountdownTimer : MonoBehaviour
{
    public float timeLeft = 420f;  // Set to 7 minutes (420 seconds)
    public TextMeshProUGUI timerText;  // TextMeshProUGUI for the timer text
    public GameObject blackScreen; // The black screen to show after the countdown
    public GameObject continueButton; // The button to continue after the countdown
    public Camera mainCamera;     // The initial camera to deactivate after the countdown
    public Camera secondaryCamera; // The camera to activate after the countdown

    public GameObject scene1Buttons; // Reference to the UI buttons for Scene 1
    public GameObject scene2Buttons; // Reference to the UI buttons for Scene 2

    private bool timerRunning = false; // Countdown won't run at the start

    void Start()
    {
        blackScreen.SetActive(false);
        continueButton.SetActive(false);
        scene1Buttons.SetActive(true); // Show buttons for Scene 1
        scene2Buttons.SetActive(false); // Hide buttons for Scene 2
        UpdateTimerDisplay(timeLeft); // Initialize the timer text in MM:SS format
    }

    void Update()
    {
        if (timerRunning)
        {
            if (timeLeft > 0)
            {
                timeLeft -= Time.deltaTime;
                UpdateTimerDisplay(timeLeft); // Update the text display in MM:SS format
            }
            else
            {
                timeLeft = 0;
                timerRunning = false;
                TimerEnded(); // Call the function to handle the timer ending
            }
        }
    }

    // Function to start the countdown when the button is clicked
    public void StartCountdown()
    {
        timerRunning = true; // Set the flag to start the timer
    }

    void TimerEnded()
    {
        blackScreen.SetActive(true);
        continueButton.SetActive(true);
        scene1Buttons.SetActive(false); // Hide Scene 1 buttons when timer ends
    }

    public void OnContinueButtonPressed()
    {
        blackScreen.SetActive(false);
        continueButton.SetActive(false);

        // Switch to the secondary camera (if needed)
        mainCamera.gameObject.SetActive(false);
        secondaryCamera.gameObject.SetActive(true);

        // Switch UI buttons
        scene1Buttons.SetActive(false); // Hide Scene 1 buttons
        scene2Buttons.SetActive(true);  // Show Scene 2 buttons
    }

    // Function to update the timer display in MM:SS format
    void UpdateTimerDisplay(float timeRemaining)
    {
        int minutes = Mathf.FloorToInt(timeRemaining / 60); // Calculate minutes
        int seconds = Mathf.FloorToInt(timeRemaining % 60); // Calculate seconds

        // Display time in MM:SS format
        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }
}
