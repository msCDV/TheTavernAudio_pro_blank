using UnityEngine;
using FMODUnity;
using FMOD.Studio;

public class FMOD_Commands : MonoBehaviour
{
    #region EVENT EMITTER
    // EVENT EMITTER
    public FMODUnity.StudioEventEmitter tavernEmitter; // �cie�ka do event emittera na scenie // ENG - path to event emitter on stage
    #endregion

    #region EVENT
    // EVENT
    FMOD.Studio.EventInstance FootstepsSound; // klasa eventu / snapshotu // ENG - event / snapshot class
    public EventReference footstepsEvent; // �cie�ka dost�pu do eventu / snapshotu // ENG - path to the event / snapshot

    private void Footsteps()
    {
        // jednorazowe odtworzenie // ENG - oneshot
        FMODUnity.RuntimeManager.PlayOneShot(footstepsEvent); // stworzenie klasy snapshotu ze �cie�k� do wybranego snapshotu i jednorazowe odtworzenie
                                                              // ENG - Create a snapshot class with the path to the selected snapshot and play it once
        // podstawowe zarz�dzanie eventem
        // ENG - basic event management
        FootstepsSound = FMODUnity.RuntimeManager.CreateInstance(footstepsEvent); // podanie klasie eventu �cie�ki do wybranego eventu / snapshotu
                                                                                  // ENG - giving the event class the path to the selected event / snapshot
        FootstepsSound.setParameterByNameWithLabel("Footsteps_surface", "Stone"); // zmiana warto�ci parametru zadeklarowanego lub wykorzystanego w evencie
                                                                                  // ENG - changing the value of a parameter declared or used in an event
        FootstepsSound.start(); // odtworzenie eventu // ENG - event playback
        FootstepsSound.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); // STOP bez fadeout // ENG - stop without fading out
        FootstepsSound.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT); // STOP z fadeout // ENG - stop with fading out
        FootstepsSound.release(); // zwolnienie pami�ci // ENG - memory release

        // zarz�dzanie eventem z przypi�ciem emittera do gameObjectu 
        // ENG - Event management with pinning the emitter to the gameObject
        FootstepsSound = FMODUnity.RuntimeManager.CreateInstance(footstepsEvent);
        FootstepsSound.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(gameObject.transform)); // !!!!11! przypi�cie emittera eventu do gameObjectu !!!11!!
                                                                                                     // ENG - !!!!11! pinning the event emitter to the gameObject !!!11!!
        FootstepsSound.setParameterByNameWithLabel("Footsteps_surface", "Stone");
        FootstepsSound.start();
        FootstepsSound.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        FootstepsSound.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        FootstepsSound.release();
    }
    #endregion

    #region SNAPSHOT
    // SNAPSHOT
    FMOD.Studio.EventInstance HealthSnap; // klasa eventu / snapshotu // ENG - event / snapshot class
    public EventReference healthSnapshot; // �cie�ka dost�pu do eventu / snapshotu // ENG - path to the event / snapshot

    private void StartSnapshot()
    {
        if (tavernEmitter != null && tavernEmitter.IsPlaying()) // sprawdzenie czy event emitter istnieje na scenie i czy jest aktywny
                                                                // ENG - checking if the event emitter exists on the scene and if it is active
        {
            HealthSnap = FMODUnity.RuntimeManager.CreateInstance(healthSnapshot); // podanie klasie snapshotu �cie�ki do wybranego eventu / snapshotu
                                                                                  // ENG - giving the snapshot class the path to the selected event / snapshot
            HealthSnap.start(); // w��czenie snapshotu // ENG - snapshot activation
        }
        else if (tavernEmitter != null && tavernEmitter.IsPlaying())
        {
            HealthSnap.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); // STOP bez fadeout // ENG - stop without fading out
            HealthSnap.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT); // STOP z fadeout // ENG - stop with fading out
            HealthSnap.release(); // zwolnienie pami�ci // ENG - memory release
        }
    }
    #endregion

    #region VCA
    // VCA
    FMOD.Studio.VCA GlobalVCA; // klasa VCA // ENG - VCA's class

    private void VCA()
    {
        GlobalVCA = FMODUnity.RuntimeManager.GetVCA("vca:/Mute"); // podanie klasie VCA �cie�ki do wybranego eventu / snapshotu
        GlobalVCA.setVolume(DecibelToLinear(0)); // ustawienie maksymalnej g�o�no�ci z przeliczeniem na decybele [dB]
        GlobalVCA.setVolume(DecibelToLinear(-100)); // obni�enie g�o�no�ci z przeliczeniem na decybele [dB]
    }

    private float DecibelToLinear(float dB) // dodatkowa funkacja spoza FMOD przeliczaj�ca zwyk�e warto�ci liczbowe na decyble [dB]
    {
        float linear = Mathf.Pow(10.0f, dB / 20f);
        return linear;
    }
    #endregion

    #region EVENT / EMITTER Z MUZYK�
    // EVENT / EMITTER Z MUZYK�
    FMOD.Studio.EventInstance Music; // klasa eventu / snapshotu
    public FMODUnity.StudioEventEmitter tavernEmitter_Music; // �cie�ka do event emittera na scenie

    private void MusicSwtich()
    {
        // EVENT
        FootstepsSound = FMODUnity.RuntimeManager.CreateInstance(footstepsEvent); // podanie klasie eventu �cie�ki do wybranego eventu / snapshotu
                                                                                  // ENG - giving the event class the path to the selected event / snapshot
        Music.setParameterByNameWithLabel("Switch_parts", "Part 2"); // zmiana warto�ci parametr�w typu labeled dla event�w
                                                                     // ENG - changing labeled parameter values for events
        Music.start(); // odtworzenie eventu // ENG - event playback
        Music.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); // STOP bez fadeout // ENG - stop without fading out
        Music.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT); // STOP z fadeout // ENG - stop with fading out
        Music.release(); // zwolnienie pami�ci


        // EMITTER
        tavernEmitter_Music.SetParameter("Switch_parts", 0); // zmiana warto�ci parametr�w typu labeled dla event emitter�w
                                                             // ENG - changing labeled parameter values for event emitters
        tavernEmitter_Music.Play(); // w��czenie odtwarzania na emitterze // ENG - enableing playback on the emitter
        tavernEmitter_Music.Stop(); // wy��czenie odtwarzania na emitterze // ENG - disabling playback on the emitter
    }
    #endregion
}
