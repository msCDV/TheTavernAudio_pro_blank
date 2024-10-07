using FMOD.Studio;
using FMODUnity;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpellCast : MonoBehaviour
{
    public GameObject spellPrefab;  // Reference to the spell particle system prefab
    public Transform spellSpawnPoint;  // The point where the spell is instantiated (e.g., player's hand or camera)
    private float startOffset;
    public GameObject spellInstance;
    public Rigidbody rb;
    public float spellSpeed; // Speed at which the spell moves
    public float spellLifetime; // How long the spell lasts before being destroyed

    private AudioSystem audioSystem;
    private PLAYBACK_STATE pb;
    private string STOPPED;

    // Start is called before the first frame update
    void Start()
    {
        startOffset = 2000f;
        spellSpeed = 200f;
        spellLifetime = 3f;

        # region KOD DO UZUPE£NIENIA // ENG - CODE TO COMPLETE

        audioSystem = ... ;
        audioSystem.SpellSound = ... ;
        STOPPED = ... ;

        #endregion
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            audioSystem.SpellSound.getPlaybackState(out pb);

            # region KOD DO UZUPE£NIENIA // ENG - CODE TO COMPLETE

            if (pb.ToString() == ...)
            {
                spellCastParticle();
                audioSystem. ... ''
            }

            # endregion KOD DO UZUPE£NIENIA // ENG - CODE TO COMPLETE

            spellInstance.transform.position = spellSpawnPoint.position;

        }
        else if (Input.GetMouseButtonUp(0))
        {

            # region KOD DO UZUPE£NIENIA // ENG - CODE TO COMPLETE
            
            spellShot();
            audioSystem. ... ;

            # endregion KOD DO UZUPE£NIENIA // ENG - CODE TO COMPLETE
        }

        audioSystem.SpellSound.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(gameObject.transform));
    }

    private void spellCastParticle()
    {
        // Instantiate the spell at the spawn point
        Vector3 spawnPosition = spellSpawnPoint.transform.position + spellSpawnPoint.transform.forward * startOffset; // ???
        spellInstance = Instantiate(spellPrefab, spawnPosition, spellSpawnPoint.rotation);

        // Add velocity to move the spell forward
        rb = spellInstance.AddComponent<Rigidbody>();
        rb.useGravity = false;  // Disable gravity so it flies straight        
    }

    private void spellShot()
    {        
        rb.velocity = spellSpawnPoint.forward * spellSpeed;

        // Destroy the spell after a set time (spellLifetime)
        Destroy(spellInstance, spellLifetime);
    }
}
