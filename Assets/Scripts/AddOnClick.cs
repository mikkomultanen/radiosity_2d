using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class AddOnClick : MonoBehaviour
{
    public Transform prefab;
	private Camera _camera;
	void Start () {
		_camera = GetComponent<Camera>();
	}
	private void Update() {
		if (Input.GetMouseButtonDown(0)) {
			Vector3 mousePos = new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0f);
			Vector3 viewPos = _camera.ScreenToViewportPoint(mousePos);
			if (viewPos.x > 0 && viewPos.x < 1 && viewPos.y > 0 && viewPos.y < 1) {
				Vector3 wordPos = _camera.ScreenToWorldPoint(mousePos);
                var go = Instantiate(prefab, new Vector3(wordPos.x, wordPos.y, prefab.position.z), Quaternion.identity);
			}
		}
	}
}
