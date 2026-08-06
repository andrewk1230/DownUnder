using UnityEngine;

public class CameraSwitcher : MonoBehaviour
{
    // Drag your cameras from the hierarchy into these slots in the inspector
    public GameObject firstPersonCam;
    public GameObject thirdPersonCam;

    // Track which perspective is currently active
    private bool isFirstPerson = false;

    void Update()
    {
        // Detects if the 'C' key is pressed. You can change this to any key.
        if (Input.GetKeyDown(KeyCode.C))
        {
            ToggleCamera();
        }
    }

    void ToggleCamera()
    {
        // Flip the boolean state
        isFirstPerson = !isFirstPerson;

        // Enable one camera and disable the other based on the boolean state
        firstPersonCam.SetActive(isFirstPerson);
        thirdPersonCam.SetActive(!isFirstPerson);
    }
}
