using FMOD.Studio;
using FMODUnity;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpellCast : MonoBehaviour
{
    private AudioSystem audioSystem;
    private PLAYBACK_STATE pb;
    private string STOPPED;

    // Start is called before the first frame update
    void Start()
    {
        audioSystem = FindObjectOfType<AudioSystem>();
        audioSystem.SpellSound = RuntimeManager.CreateInstance(audioSystem.spellEvent);
        STOPPED = "STOPPED";
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            audioSystem.SpellSound.getPlaybackState(out pb);

            if (pb.ToString() == STOPPED)
                audioSystem.SpellCast();
        }
        else if (Input.GetMouseButtonUp(0))
        {
            audioSystem.SpellRelease();
        }

        audioSystem.SpellSound.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(gameObject.transform));
    }
}
