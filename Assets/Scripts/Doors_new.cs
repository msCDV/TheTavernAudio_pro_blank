using System.Collections;
using UnityEngine;

public class DoorsNew : MonoBehaviour, IInteractable
{
    private AudioSystem _audioSystem;

    public float rotationSpeed = 90f;
    private bool _doorsOpened = true;
    private bool _isRotating;

    private void Start()
    {
        _audioSystem = FindObjectOfType<AudioSystem>();
    }

    public void Interact()
    {
        if (!_isRotating)
        {
            DoorsInteract();
        }
    }

    private IEnumerator CloseOverTime()
    {
        _isRotating = true;
        var elapsedTime = 0f;
        var startRotation = transform.rotation;
        var targetRotation = Quaternion.Euler(0, 65, 0) * startRotation;

        while (elapsedTime < 1f)
        {
            transform.rotation = Quaternion.Lerp(startRotation, targetRotation, elapsedTime);
            elapsedTime += Time.deltaTime * rotationSpeed / 90f;
            yield return null;
        }

        transform.rotation = targetRotation;
        _isRotating = false;
    }

    private IEnumerator OpenOverTime()
    {
        _isRotating = true;
        var elapsedTime = 0f;
        var startRotation = transform.rotation;
        var targetRotation = Quaternion.Euler(0, -65, 0) * startRotation;

        while (elapsedTime < 1f)
        {
            transform.rotation = Quaternion.Lerp(startRotation, targetRotation, elapsedTime);
            elapsedTime += Time.deltaTime * rotationSpeed / 90f;
            yield return null;
        }

        transform.rotation = targetRotation;
        _isRotating = false;
    }

    private void DoorsInteract()
    {
        if (_doorsOpened)
        {
            StartCoroutine(CloseOverTime());
            _audioSystem.doorsName = gameObject.name;
            _audioSystem.PlayDoorSound();
            _doorsOpened = false;
            if (_audioSystem.roomsAmbientActivated)
                _audioSystem.RoomsSnap();
        }
        else
        {
            StartCoroutine(OpenOverTime());
            _audioSystem.doorsName = gameObject.name;
            _audioSystem.PlayDoorSound();
            _doorsOpened = true;
            if (_audioSystem.roomsAmbientActivated)
                _audioSystem.RoomsSnap();
        }
    }
}