using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Door : MonoBehaviour
{
	public Animator anim;

	// Use this for initialization
	private void Start ()
	{
		anim = GetComponent<Animator> ();
	}

	private void OnTriggerEnter (Collider other)
	{
		Debug.Log("entered");
		anim.SetBool ("DoorOpen", true);
		anim.SetBool ("DoorClose", false);
	}

	private void OnTriggerExit (Collider other)
	{
		anim.SetBool ("DoorOpen", false);
		anim.SetBool ("DoorClose", true);
	}
}
