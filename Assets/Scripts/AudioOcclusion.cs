using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using UnityEngine.Serialization;

public class AudioOcclusion : MonoBehaviour
{
    [Header("FMOD Event")] [SerializeField]
    private StudioEventEmitter eventEmitterMusic;

    private EventInstance _eventInstance;
    private EventDescription _eventDes;
    private StudioListener _listener;
    private PLAYBACK_STATE _pb;

    [FormerlySerializedAs("SoundOcclusionWidening")] [Header("Occlusion Options")] [SerializeField] [Range(0f, 10f)]
    private float soundOcclusionWidening = 1f;

    [FormerlySerializedAs("PlayerOcclusionWidening")] [SerializeField] [Range(0f, 10f)]
    private float playerOcclusionWidening = 1f;

    [FormerlySerializedAs("OcclusionLayer")] [SerializeField]
    private LayerMask occlusionLayer;

    private bool _audioIsVirtual;
    private float _minDistance;
    private float _maxDistance;
    private float _listenerDistance;
    private float _lineCastHitCount;
    private Color _colour;

    private void Start()
    {
        _eventInstance = eventEmitterMusic.EventInstance;
        _eventInstance.getDescription(out _eventDes);
        _eventDes.getMinMaxDistance(out _minDistance, out _maxDistance);

        _listener = FindObjectOfType<StudioListener>();
    }

    private void FixedUpdate()
    {
        _eventInstance.isVirtual(out _audioIsVirtual);
        _eventInstance.getPlaybackState(out _pb);
        _listenerDistance = Vector3.Distance(transform.position, _listener.transform.position);

        if (!_audioIsVirtual && _pb == PLAYBACK_STATE.PLAYING && _listenerDistance <= _maxDistance)
            OccludeBetween(transform.position, _listener.transform.position);

        _lineCastHitCount = 0f;
    }

    private void OccludeBetween(Vector3 sound, Vector3 listener)
    {
        var soundLeft = CalculatePoint(sound, listener, soundOcclusionWidening, true);
        var soundRight = CalculatePoint(sound, listener, soundOcclusionWidening, false);

        var soundAbove = new Vector3(sound.x, sound.y + soundOcclusionWidening, sound.z);
        var soundBelow = new Vector3(sound.x, sound.y - soundOcclusionWidening, sound.z);

        var listenerLeft = CalculatePoint(listener, sound, playerOcclusionWidening, true);
        var listenerRight = CalculatePoint(listener, sound, playerOcclusionWidening, false);

        var listenerAbove = new Vector3(listener.x, listener.y + playerOcclusionWidening * 0.5f, listener.z);
        var listenerBelow = new Vector3(listener.x, listener.y - playerOcclusionWidening * 0.5f, listener.z);

        CastLine(soundLeft, listenerLeft);
        CastLine(soundLeft, listener);
        CastLine(soundLeft, listenerRight);

        CastLine(sound, listenerLeft);
        CastLine(sound, listener);
        CastLine(sound, listenerRight);

        CastLine(soundRight, listenerLeft);
        CastLine(soundRight, listener);
        CastLine(soundRight, listenerRight);

        CastLine(soundAbove, listenerAbove);
        CastLine(soundBelow, listenerBelow);

        if (playerOcclusionWidening == 0f || soundOcclusionWidening == 0f)
        {
            _colour = Color.blue;
        }
        else
        {
            _colour = Color.green;
        }

        SetParameter();
    }

    private static Vector3 CalculatePoint(Vector3 a, Vector3 b, float m, bool posOrneg)
    {
        float x;
        float z;
        var n = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
        var mn = (m / n);
        if (posOrneg)
        {
            x = a.x + (mn * (a.z - b.z));
            z = a.z - (mn * (a.x - b.x));
        }
        else
        {
            x = a.x - (mn * (a.z - b.z));
            z = a.z + (mn * (a.x - b.x));
        }

        return new Vector3(x, a.y, z);
    }

    private void CastLine(Vector3 start, Vector3 end)
    {
        Physics.Linecast(start, end, out var hit, occlusionLayer);

        if (hit.collider)
        {
            _lineCastHitCount++;
            Debug.DrawLine(start, end, Color.red);
        }
        else
            Debug.DrawLine(start, end, _colour);
    }

    private void SetParameter()
    {
        _eventInstance.setParameterByName("Occlusion", _lineCastHitCount / 11);
    }
}