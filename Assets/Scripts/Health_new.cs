using UnityEngine;

public class HealthNew : MonoBehaviour
{
    private AudioSystem _audioSystem;

    private void Start()
    {
        _audioSystem = GetComponent<AudioSystem>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.K))
        {
            _audioSystem.HealthSnap();
        }
    }
}