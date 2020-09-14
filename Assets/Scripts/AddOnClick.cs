using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class AddOnClick : MonoBehaviour
{
    public Transform prefab;
	[Range(0, 1)]
	public float interval = 0.1f;
	private Camera _camera;
	private float delta = 0f;
	void Start () {
		_camera = GetComponent<Camera>();
	}
	private void Update() {
		delta -= Time.deltaTime;
		if (Input.GetMouseButton(0) && delta < 0f) {
			delta = interval;
			Vector3 mousePos = new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0f);
			Vector3 viewPos = _camera.ScreenToViewportPoint(mousePos);
			if (viewPos.x > 0 && viewPos.x < 1 && viewPos.y > 0 && viewPos.y < 1) {
				Vector3 wordPos = _camera.ScreenToWorldPoint(mousePos);
                var go = Instantiate(prefab, new Vector3(wordPos.x, wordPos.y, prefab.position.z), Quaternion.identity);
				var mp = go.GetComponent<MaterialProperties>();
				if (mp) {
					float r = Random.Range(0f, 1f);
					float g = Random.Range(0f, 1f);
					float b = Random.Range(0f, 1f);
					mp.color = new Color(r, g, b, 1f);
				}
			}
		}
	}
}
