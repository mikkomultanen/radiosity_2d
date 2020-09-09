using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode()]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RadiositySystem : MonoBehaviour
{
	public float size = 256;
    public int textureSize = 1024;
    [Range(0, 256)]
    public int sweeps = 128;
    [Range(0, 256)]
    public int sweepsPerFrame = 16;
    [Range(0, 1)]
    public float feedback = 0.9f;
    [Range(0, 1)]
    public float multiplier = 0.1f;
	public float nearClip = 0.01f;
    public float farClip = 500;	
	public LayerMask layerMask;
	public RenderTexture lightTexture;
    private ComputeBuffer[] lightBuffer = new ComputeBuffer[2];
    private ComputeShader radiosityShader;
    private int clearKernel;
    private int sweepKernel;
    private int copyToTexKernel;
	private Camera cam;
    private int sweep;

    private void Awake()
    {
        GenerateMesh();
    }

#if UNITY_EDITOR
    [ContextMenu("Generate mesh")]
#endif
    private void GenerateMesh()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
		Mesh mesh = meshFilter.sharedMesh;
		if (mesh == null) {
			meshFilter.mesh = new Mesh ();
			mesh = meshFilter.sharedMesh;
		}

        var vertices = new Vector3[4];
        var halfSize = size / 2;

        vertices[0] = new Vector3(-halfSize, -halfSize, 0);
        vertices[1] = new Vector3(halfSize, -halfSize, 0);
        vertices[2] = new Vector3(-halfSize, halfSize, 0);
        vertices[3] = new Vector3(halfSize, halfSize, 0);

        mesh.vertices = vertices;

        var tri = new int[6];

        tri[0] = 0;
        tri[1] = 2;
        tri[2] = 1;

        tri[3] = 2;
        tri[4] = 3;
        tri[5] = 1;

        mesh.triangles = tri;

        var uv = new Vector2[4];

        uv[0] = new Vector2(0, 0);
        uv[1] = new Vector2(1, 0);
        uv[2] = new Vector2(0, 1);
        uv[3] = new Vector2(1, 1);

        mesh.uv = uv;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
    }

    private void OnEnable() {
		lightTexture = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
		lightTexture.filterMode = FilterMode.Bilinear;
        lightTexture.enableRandomWrite = true;
		lightTexture.hideFlags = HideFlags.DontSave;
        lightTexture.Create();
        Shader.SetGlobalTexture("LightTex", lightTexture);
        Shader.SetGlobalVector("LightTexScale", new Vector4(1.0f / size, 1.0f / size, size, size));

        lightBuffer[0] = new ComputeBuffer(textureSize * textureSize, Marshal.SizeOf(typeof(Vector4)), ComputeBufferType.Default);
        lightBuffer[1] = new ComputeBuffer(textureSize * textureSize, Marshal.SizeOf(typeof(Vector4)), ComputeBufferType.Default);
        radiosityShader = Resources.Load("Radiosity") as ComputeShader;
        clearKernel = radiosityShader.FindKernel("CSClear");
        sweepKernel = radiosityShader.FindKernel("CSSweep");
        copyToTexKernel = radiosityShader.FindKernel("CSCopyToTexture");

		GameObject go = new GameObject("DistanceFieldCamera", typeof(Camera));//, typeof(RadiosityTest));
		go.hideFlags = HideFlags.HideAndDontSave;
		go.transform.position = transform.position;
		go.transform.rotation = Quaternion.identity;
		cam = go.GetComponent<Camera>();
        cam.allowMSAA = true;
		cam.cullingMask = layerMask;
        var halfSize = size / 2;
		cam.backgroundColor = Color.clear;
		cam.clearFlags = CameraClearFlags.SolidColor;
		cam.projectionMatrix = Matrix4x4.Ortho(-halfSize, halfSize, -halfSize, halfSize, nearClip, farClip);
		cam.enabled = false;

		//cam.SetReplacementShader(Shader.Find("Hidden/PlanetShooter/DistanceField"),"");
		MeshRenderer renderer = GetComponent<MeshRenderer>();
        MaterialPropertyBlock materialProperties = new MaterialPropertyBlock();
        materialProperties.SetTexture("_MainTex", lightTexture);
        renderer.SetPropertyBlock(materialProperties);
	}

	private void LateUpdate() {
		RenderTexture shapes = RenderTexture.GetTemporary(textureSize, textureSize, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear, 1);
		shapes.filterMode = FilterMode.Point;
		cam.targetTexture = shapes;
		cam.Render();
		cam.targetTexture = null;

        radiosityShader.SetBuffer(clearKernel, "LightBufferR", lightBuffer[0]);
        radiosityShader.SetBuffer(clearKernel, "LightBufferW", lightBuffer[1]);
        radiosityShader.SetInt("TexSize", textureSize);
        radiosityShader.Dispatch(clearKernel, textureSize / 8, textureSize / 8, 1);

        Swap(lightBuffer);

        radiosityShader.SetTexture(sweepKernel, "ShapesTexR", shapes);
        radiosityShader.SetInt("TexSize", textureSize);
        radiosityShader.SetFloat("FeedbackMultiplier", feedback);
        radiosityShader.SetFloat("Multiplier", multiplier / sweepsPerFrame);
        float rndAngle = 0f;//Random.Range(-0.5f, 0.5f);
        for (int k = 0; k < sweepsPerFrame / 4; ++k)
        {
            for (int q = 0; q < 4; ++q)
            {
                Vector4 qStart;
                switch(q) {
                    case 0: 
                        qStart = new Vector4(0, 0, 1, 1);
                        break;
                    case 1:
                        qStart = new Vector4(textureSize, 0, -1, 1);
                        break;
                    case 2:
                        qStart = new Vector4(textureSize, textureSize, -1, -1);
                        break;
                    default:
                        qStart = new Vector4(0, textureSize, 1, -1);
                        break;
                }
                float angle = 0.5f * Mathf.PI * ((sweep + 0.5f + rndAngle) / sweeps + q);
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector4 start;
                var dx = Mathf.Abs(dir.x);
                var dy = Mathf.Abs(dir.y);
                int lines = textureSize;
                if (dx > dy) {
                    dir /= dx;
                    start = new Vector4(qStart.x, qStart.y - dir.y * textureSize, 0, qStart.w);
                    lines = textureSize + Mathf.CeilToInt(Mathf.Abs(start.y));
                } else {
                    dir /= dy;
                    start = new Vector4(qStart.x - dir.x * textureSize, qStart.y, qStart.z, 0);
                    lines = textureSize + Mathf.CeilToInt(Mathf.Abs(start.x));
                }
                radiosityShader.SetBuffer(sweepKernel, "LightBufferR", lightBuffer[0]);
                radiosityShader.SetBuffer(sweepKernel, "LightBufferW", lightBuffer[1]);
                radiosityShader.SetVector("SweepStart", start);
                radiosityShader.SetVector("SweepStep", dir);
                radiosityShader.Dispatch(sweepKernel, lines / 8, 1, 1);
                Swap(lightBuffer);
            }
            sweep = (sweep + 4) % sweeps;
        }

        //Graphics.Blit(shapes, lightTexture);
        RenderTexture.ReleaseTemporary(shapes);

        radiosityShader.SetBuffer(copyToTexKernel, "LightBufferR", lightBuffer[0]);
        radiosityShader.SetTexture(copyToTexKernel, "LightTexW", lightTexture);
        radiosityShader.SetInt("TexSize", textureSize);
        radiosityShader.Dispatch(copyToTexKernel, textureSize / 8, textureSize / 8, 1);

	}

	private void OnDisable() {
		DestroyImmediate(cam);
		DestroyImmediate(lightTexture);
        Release(lightBuffer);
	}

    void OnGUI()
    {
        var text = "Supported MRT count: ";
        text += SystemInfo.supportedRenderTargetCount;
        GUI.Label(new Rect(0, 0, 200, 200), text);
    }

    public static void Swap(ComputeBuffer[] buffers)
    {
        ComputeBuffer tmp = buffers[0];
        buffers[0] = buffers[1];
        buffers[1] = tmp;
    }

    public static void Release(IList<ComputeBuffer> buffers)
    {
        if (buffers == null) return;

        int count = buffers.Count;
        for (int i = 0; i < count; i++)
        {
            if (buffers[i] == null) continue;
            buffers[i].Release();
            buffers[i] = null;
        }
    }
}
