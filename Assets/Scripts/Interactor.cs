using UnityEngine;
using UnityEngine.Serialization;

internal interface IInteractable
{
    public void Interact();
}

public class Interactor : MonoBehaviour
{
    [FormerlySerializedAs("InteractorSource")] public Transform interactorSource;
    [FormerlySerializedAs("InteractRange")] public float interactRange;

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.E)) return;
        var r = new Ray(interactorSource.position, interactorSource.forward);
        if (!Physics.Raycast(r, out var hitInfo, interactRange)) return;
        if (hitInfo.collider.gameObject.TryGetComponent(out IInteractable interactObj))
        {
            interactObj.Interact();
        }
    }
}