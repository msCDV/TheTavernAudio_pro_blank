using UnityEngine;

public class MusicSwitchNew : MonoBehaviour, IInteractable
{
    private AudioSystem _audioSystem;

    private void Start()
    {
        _audioSystem = FindObjectOfType<AudioSystem>();
    }

    public void Interact()
    {
        if (_audioSystem.isMusicPlaying)
        {
            switch (gameObject.name)
            {
                case "Food_bottle4":
                    _audioSystem.tavernMusic.SetParameter("Switch_parts", 0);
                    Debug.Log("Switching Music");
                    break;
                case "Food_bottle1":
                    _audioSystem.tavernMusic.SetParameter("Switch_parts", 1);
                    break;
                case "Food_bottle3":
                    _audioSystem.tavernMusic.SetParameter("Switch_parts", 2);
                    break;
                case "Food_bottle2":
                    _audioSystem.tavernMusic.SetParameter("Switch_parts", 3);
                    _audioSystem.isMusicPlaying = false;
                    break;
            }
        }
        else if (gameObject.name == "Food_bottle6" && !_audioSystem.isMusicPlaying)
        {
            if (gameObject.name == "Food_bottle6")
                _audioSystem.tavernMusic.SetParameter("Switch_parts", 0);
            _audioSystem.tavernMusic.Play();
            _audioSystem.isMusicPlaying = true;
        }
    }
}