using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode()]
[RequireComponent(typeof(Renderer))]
public class MaterialProperties : MonoBehaviour
{
[ColorUsageAttribute(true, true)]
    public Color color;
	private Renderer _renderer;
	private MaterialPropertyBlock propBlock;
	private int colorNameId;

	private void OnEnable() {
		_renderer = GetComponent<Renderer>();
		propBlock = new MaterialPropertyBlock();
		colorNameId = Shader.PropertyToID("_Color");
		UpdatePropBlock();
    }

    private void Start() {
		UpdatePropBlock();
    }
	
	private void UpdatePropBlock() {
		_renderer.SetPropertyBlock(propBlock);
		propBlock.SetColor(colorNameId, color);
		_renderer.SetPropertyBlock(propBlock);
	}
}
