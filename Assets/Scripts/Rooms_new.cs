using UnityEngine;

public class RoomsNew : MonoBehaviour
{
    private AudioSystem _audioSystem;

    private void Start()
    {
        _audioSystem = FindObjectOfType<AudioSystem>();
    }

    private void OnTriggerStay(Collider other)
    {
        _audioSystem.RoomsAmbientOn();
    }

    private void OnTriggerExit(Collider other)
    {
        _audioSystem.RoomsAmbientOff();
    }
}