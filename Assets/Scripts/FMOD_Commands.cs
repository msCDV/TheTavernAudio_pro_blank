using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using UnityEngine.Serialization;

public class FMODCommands : MonoBehaviour
{
    #region EVENT EMITTER

    public StudioEventEmitter tavernEmitter;

    #endregion

    #region EVENT

    private EventInstance _footstepsSound;
    public EventReference footstepsEvent;

    private void Footsteps()
    {
        RuntimeManager.PlayOneShot(footstepsEvent);
        _footstepsSound = RuntimeManager.CreateInstance(footstepsEvent);
        _footstepsSound.setParameterByNameWithLabel("Footsteps_surface", "Stone");
        _footstepsSound.start();
        _footstepsSound.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        _footstepsSound.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        _footstepsSound.release();

        _footstepsSound = RuntimeManager.CreateInstance(footstepsEvent);
        _footstepsSound.set3DAttributes(gameObject.transform.To3DAttributes());
        _footstepsSound.setParameterByNameWithLabel("Footsteps_surface", "Stone");
        _footstepsSound.start();
        _footstepsSound.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        _footstepsSound.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        _footstepsSound.release();
    }

    #endregion

    #region SNAPSHOT

    private EventInstance _healthSnap;
    private EventReference _healthSnapshot;

    private void StartSnapshot()
    {
        if (tavernEmitter != null && tavernEmitter.IsPlaying())
        {
            _healthSnap = RuntimeManager.CreateInstance(_healthSnapshot);
            _healthSnap.start();
        }
        else if (tavernEmitter != null && tavernEmitter.IsPlaying())
        {
            _healthSnap.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            _healthSnap.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _healthSnap.release();
        }
    }

    #endregion

    #region VCA

    private VCA _globalVca;

    private void Vca()
    {
        _globalVca = RuntimeManager.GetVCA("vca:/Mute");
        _globalVca.setVolume(DecibelToLinear(0));
        _globalVca.setVolume(DecibelToLinear(-100));
    }

    private static float DecibelToLinear(float dB)
    {
        var linear = Mathf.Pow(10.0f, dB / 20f);
        return linear;
    }

    #endregion

    #region EVENT / EMITTER Z MUZYK¥

    private EventInstance _music;
    [FormerlySerializedAs("tavernEmitter_Music")] public StudioEventEmitter tavernEmitterMusic;

    private void MusicSwitch()
    {
        _footstepsSound = RuntimeManager.CreateInstance(footstepsEvent);
        _music.setParameterByNameWithLabel("Switch_parts", "Part 2");
        _music.start();
        _music.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        _music.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        _music.release();

        tavernEmitterMusic.SetParameter("Switch_parts", 0);
        tavernEmitterMusic.Play();
        tavernEmitterMusic.Stop();
    }

    #endregion
}