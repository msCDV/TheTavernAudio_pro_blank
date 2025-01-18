using UnityEngine;

public class FireplacePlaybackNew : MonoBehaviour
{
    private AudioSystem _audioSystem;

    private void Start()
    {
        _audioSystem = FindObjectOfType<AudioSystem>();
    }

    private void OnTriggerStay(Collider other)
    {
        if (_audioSystem != null)
        {
            _audioSystem.FireplaceOff();
        }
        else
        {
            Debug.LogError("audioSystem reference is not set.");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        _audioSystem.FireplaceOn();
    }
}