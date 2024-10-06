using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Fireplace_playback_new : MonoBehaviour
{
    private AudioSystem audioSystem;

    private void Start()
    {        
        audioSystem = FindObjectOfType<AudioSystem>();
    }

    private void OnTriggerStay(Collider other)
    {
        if (audioSystem != null)
        {
            audioSystem.FireplaceOFF();
        }
        else
        {
            Debug.LogError("audioSystem reference is not set.");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        audioSystem.FireplaceON();
    }
}
