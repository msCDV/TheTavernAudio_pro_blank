using FMOD.Studio;
using FMODUnity;
using UnityEngine;

public class SpellCast : MonoBehaviour
{
    private AudioSystem _audioSystem;
    private PLAYBACK_STATE _pb;
    private bool _lockFlag;

    private const string EventName = "throwSpell";
    private const string EventLabelOff = "false";
    private const string EventLabelOn = "true";

    private void Start()
    {
        _audioSystem = FindObjectOfType<AudioSystem>();
        _audioSystem.SpellSound = RuntimeManager.CreateInstance(_audioSystem.spellEvent);
    }

    private void Update()
    {
        _audioSystem.SpellSound.getPlaybackState(out _pb);

        if (_lockFlag && _pb == PLAYBACK_STATE.STOPPED) _lockFlag = false;
        if (_lockFlag) return;

        if (Input.GetMouseButton(0) && _pb == PLAYBACK_STATE.STOPPED) _audioSystem.SpellCast(EventName, EventLabelOff);
        if (Input.GetMouseButtonUp(0))
        {
            _lockFlag = true;
            _audioSystem.SpellRelease(EventName, EventLabelOn);
        }

        _audioSystem.SpellSound.set3DAttributes(gameObject.transform.To3DAttributes());
    }
}