using UnityEngine;

public class VcaNew : MonoBehaviour
{
    private AudioSystem _audioSystem;

    private void Start()
    {
        _audioSystem = GetComponent<AudioSystem>();
    }

    private void Update()
    {
        AudioSystem.ToggleMute(KeyCode.U, ref _audioSystem.muteActive, _audioSystem.GlobalVca);
        AudioSystem.ToggleMute(KeyCode.I, ref _audioSystem.musicMuteActive, _audioSystem.MusicVca);
        AudioSystem.ToggleMute(KeyCode.O, ref _audioSystem.tavernMuteActive, _audioSystem.TavernVca);
        AudioSystem.ToggleMute(KeyCode.P, ref _audioSystem.outsideMuteActive, _audioSystem.OutsideVca);
    }
}