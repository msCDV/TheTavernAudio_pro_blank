using UnityEngine;

public class OutsideFootSwitchNew : MonoBehaviour
{
    private AudioSystem _audioSystem;

    private void Start()
    {
        _audioSystem = GetComponent<AudioSystem>();
    }

    private void FixedUpdate()
    {
        _audioSystem.OutsideSnap();
    }
}