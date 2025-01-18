using UnityEngine;

public class FootstepsNew : MonoBehaviour
{
    private AudioSystem _audioSystem;

    private float _lastFootstepTime;

    private void Start()
    {
        _audioSystem = FindObjectOfType<AudioSystem>();
    }

    private void Update()
    {
        Jump();
    }

    private void FixedUpdate()
    {
        Walking();
        Running();
    }

    private void Walking()
    {
        if (Input.GetAxisRaw("Horizontal") == 0 && Input.GetAxisRaw("Vertical") == 0) return;
        if (!_audioSystem.IsGrounded() || !(Time.time - _lastFootstepTime > 0.5f)) return;
        _lastFootstepTime = Time.time;
        _audioSystem.PlayFootsteps();
    }

    private void Running()
    {
        if ((!Input.GetKey(KeyCode.LeftShift) || Input.GetAxisRaw("Horizontal") == 0) &&
            (!Input.GetKey(KeyCode.LeftShift) || Input.GetAxisRaw("Vertical") == 0)) return;
        if (!_audioSystem.IsGrounded() || !(Time.time - _lastFootstepTime > 0.25f)) return;
        _lastFootstepTime = Time.time;
        _audioSystem.PlayFootsteps();
    }

    private void Jump()
    {
        if (!Input.GetKeyDown(KeyCode.Space)) return;
        _audioSystem.PlayJump();
    }

    private void OnCollisionEnter(Collision col)
    {
        if (_audioSystem.IsGrounded() && _audioSystem.isGrounded == false)
        {
            _audioSystem.PlayLanding();
        }
    }
}