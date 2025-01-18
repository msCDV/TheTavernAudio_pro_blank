using System;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using UnityEngine.Serialization;

public class AudioSystem : MonoBehaviour
{
    [FormerlySerializedAs("TavernMusic")] public StudioEventEmitter tavernMusic;

    [FormerlySerializedAs("TavernFireplace")]
    public StudioEventEmitter tavernFireplace;

    private EventInstance _doorsSound;
    public EventReference doorsEvent;
    private EventInstance _footstepsSound;
    public EventReference footstepsEvent;
    private EventInstance _jumpSound;
    public EventReference jumpEvent;
    private EventInstance _landSound;
    public EventReference landEvent;
    public EventInstance SpellSound;
    public EventReference spellEvent;

    private EventInstance _insideRoom;
    public EventReference insideRoomSnap;
    private EventInstance _outside;
    public EventReference outsideSnapshot;
    private EventInstance _healthSnapshot;
    public EventReference healthSnapshot;

    public VCA GlobalVca;
    public VCA MusicVca;
    public VCA TavernVca;
    public VCA OutsideVca;

    private const string FootstepsSurface = "Footsteps_surface";
    private const string Open = "Open";
    private const string Close = "Close";
    private const string Door1 = "Tavern_door_room";
    private const string Door2 = "Tavern_door_room (1)";
    private const string Door3 = "Tavern_door_room (2)";
    public string doorsName;

    private bool _doorsOpened1 = true;
    private bool _doorsOpened2 = true;
    private bool _doorsOpened3 = true;
    public bool isGrounded = true;
    private bool _isJumping;
    private bool _outsideSnapActivated;
    public bool roomsAmbientActivated;
    public bool isMusicPlaying = true;
    public bool muteActive;
    public bool musicMuteActive;
    public bool tavernMuteActive;
    public bool outsideMuteActive;
    private bool _healthSnapActive;
    private PLAYBACK_STATE _spellPb;

    public float distToGround;

    private void Start()
    {
        GlobalVca = RuntimeManager.GetVCA("vca:/Mute");
        MusicVca = RuntimeManager.GetVCA("vca:/Music");
        TavernVca = RuntimeManager.GetVCA("vca:/Tavern_amb");
        OutsideVca = RuntimeManager.GetVCA("vca:/Outside_amb");
        GlobalVca.setVolume(DecibelToLinear(-100));
        muteActive = true;
        distToGround = GetComponent<Collider>().bounds.extents.y;
        if (tavernFireplace == null) Debug.LogError("tavernFireplace is null");
    }

    public bool IsGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, distToGround + 0.5f);
    }

    private static float DecibelToLinear(float dB)
    {
        var linear = Mathf.Pow(10.0f, dB / 20f);
        return linear;
    }

    public void RoomsAmbientOn()
    {
        roomsAmbientActivated = true;
    }

    public void RoomsAmbientOff()
    {
        roomsAmbientActivated = false;
    }

    public void FireplaceOff()
    {
        tavernFireplace.SetParameter("Fire", 0);
    }

    public void FireplaceOn()
    {
        tavernFireplace.SetParameter("Fire", 1);
    }

    private void DoorsManager(ref EventInstance doorSoundInstance, int doorsNumber, string doorState)
    {
        doorSoundInstance = RuntimeManager.CreateInstance(doorsEvent);
        doorSoundInstance.setParameterByNameWithLabel("Doors", doorState);
        doorSoundInstance.set3DAttributes(gameObject.transform.To3DAttributes());
        doorSoundInstance.start();
        doorSoundInstance.release();

        switch (doorsNumber)
        {
            case 1:
                _doorsOpened1 = !_doorsOpened1;
                break;
            case 2:
                _doorsOpened2 = !_doorsOpened2;
                break;
            case 3:
                _doorsOpened3 = !_doorsOpened3;
                break;
        }
    }

    public void PlayDoorSound()
    {
        if (doorsName == Door1)
        {
            if (_doorsOpened1)
                DoorsManager(ref _doorsSound, 1, Close);
            else
                DoorsManager(ref _doorsSound, 1, Open);
        }
        else if (doorsName == Door2)
        {
            if (_doorsOpened2)
                DoorsManager(ref _doorsSound, 2, Close);
            else
                DoorsManager(ref _doorsSound, 2, Open);
        }
        else if (doorsName == Door3)
        {
            if (_doorsOpened3)
                DoorsManager(ref _doorsSound, 3, Close);
            else
                DoorsManager(ref _doorsSound, 3, Open);
        }
    }

    // FOOTSTEPS SOUNDS // 
    public void PlayFootsteps()
    {
        if (!Physics.Raycast(transform.position, Vector3.down, out var hit, distToGround + 0.5f)) return;
        var surfaceType = hit.collider.tag switch
        {
            "Wood" => "Wood",
            "Stone" or "Outside" or "Inside_stone" => "Stone",
            _ => "Stone"
        };

        _footstepsSound = RuntimeManager.CreateInstance(footstepsEvent);
        _footstepsSound.set3DAttributes(gameObject.transform.To3DAttributes());
        _footstepsSound.setParameterByNameWithLabel(FootstepsSurface, surfaceType);
        _footstepsSound.start();
        _footstepsSound.release();
    }

    // JUMP SOUNDS //
    public void PlayJump()
    {
        if (!IsGrounded()) return;
        _jumpSound = RuntimeManager.CreateInstance(jumpEvent); // "event:/Footsteps"

        if (IsGrounded())
        {
            if (Physics.Raycast(transform.position, Vector3.down, out var hit, distToGround + 0.5f))
            {
                var surface = hit.collider.tag switch
                {
                    "Stone" => "Stone",
                    "Wood" => "Wood",
                    "Inside_stone" => "Stone",
                    "Bed" => "Bed",
                    _ => "Stone"
                };

                _jumpSound.setParameterByNameWithLabel(FootstepsSurface, surface);
                _jumpSound.start();
            }
        }

        _jumpSound.release();
        isGrounded = false;
        _isJumping = true;
    }

    // LAND SOUNDS //
    public void PlayLanding()
    {
        if (!IsGrounded() || isGrounded) return;
        if (!_isJumping) return;
        _landSound = RuntimeManager.CreateInstance(landEvent);

        if (Physics.Raycast(transform.position, Vector3.down, out var hit, distToGround + 0.5f))
        {
            var surface = hit.collider.tag switch
            {
                "Stone" => "Stone",
                "Wood" => "Wood",
                "Inside_stone" => "Stone",
                "Bed" => "Bed",
                _ => "Stone"
            };

            _landSound.setParameterByNameWithLabel(FootstepsSurface, surface);
            _landSound.start();
        }

        _landSound.release();
        isGrounded = true;
        _isJumping = false;
    }

    public void SpellCast(string paramName, string label)
    {
        SpellSound = RuntimeManager.CreateInstance(spellEvent);
        SpellSound.setParameterByNameWithLabel(paramName, label);
        SpellSound.start();
        SpellSound.release();
    }

    public void SpellRelease(string paramName, string label)
    {
        SpellSound.setParameterByNameWithLabel(paramName, label);
        SpellSound.release();
    }

    public void OutsideSnap()
    {
        if (!Physics.Raycast(transform.position, Vector3.down, out var hit, distToGround + 0.5f)) return;
        if (hit.collider.CompareTag("Outside") && _outsideSnapActivated == false)
        {
            _outside = RuntimeManager.CreateInstance(outsideSnapshot);
            _outside.start();
            _outsideSnapActivated = !_outsideSnapActivated;
            Debug.Log(_outsideSnapActivated);
        }
        else if (hit.collider.CompareTag("Inside_stone") && _outsideSnapActivated)
        {
            _outside.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _outside.release();
            _outsideSnapActivated = !_outsideSnapActivated;
            Debug.Log(_outsideSnapActivated);
        }
    }

    private void RoomsSnapInstanceStart()
    {
        Debug.Log("doors closed");
        _insideRoom.start();
        _insideRoom.release();
    }

    private void RoomsSnapInstanceStop()
    {
        Debug.Log("doors opened");
        _insideRoom.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        _insideRoom.release();
    }

    public void RoomsSnap()
    {
        Debug.LogError(_insideRoom.isValid());

        if (!_insideRoom.isValid())
        {
            _insideRoom = RuntimeManager.CreateInstance(insideRoomSnap);
        }

        switch (roomsAmbientActivated)
        {
            case true when doorsName == Door1 && _doorsOpened1 == false:
            case true when doorsName == Door2 && _doorsOpened2 == false:
            case true when doorsName == Door3 && _doorsOpened3 == false:
                RoomsSnapInstanceStart();
                break;
            default:
                RoomsSnapInstanceStop();
                break;
        }
    }

    public void HealthSnap()
    {
        switch (_healthSnapActive)
        {
            case false:
                _healthSnapshot = RuntimeManager.CreateInstance(healthSnapshot);
                _healthSnapshot.start();
                _healthSnapActive = !_healthSnapActive;
                break;
            case true:
                _healthSnapshot.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                _healthSnapshot.release();
                _healthSnapActive = !_healthSnapActive;
                break;
        }
    }

    public static void ToggleMute(KeyCode key, ref bool muteActive, VCA vca)
    {
        if (!Input.GetKeyDown(key)) return;
        float volume = muteActive ? 0 : -100;
        vca.setVolume(DecibelToLinear(volume));
        muteActive = !muteActive;
    }
}