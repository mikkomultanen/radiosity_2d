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
    public RenderTextureFormat textureFormat = RenderTextureFormat.DefaultHDR;
    [Range(4, 512)]
    public int sweeps = 128;
    [Range(4, 256)]
    public int sweepsPerFrame = 16;
    [Range(0, 1)]
    public float feedback = 0.9f;
    [Range(0, 1)]
    public float multiplier = 0.1f;
	public float nearClip = 0.01f;
    public float farClip = 500;	
	public LayerMask layerMask;
	private RenderTexture[] lightBuffer = new RenderTexture[2];
    private ComputeShader radiosityShader;
    private int clearKernel;
    private int sweepKernel;
	private Camera cam;
    private int sweep;
    private int[] order;
    private float avgLPS = 0f;

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
		lightBuffer[0] = new RenderTexture(textureSize, textureSize, 24, textureFormat, RenderTextureReadWrite.Linear);
		lightBuffer[0].filterMode = FilterMode.Bilinear;
        lightBuffer[0].enableRandomWrite = true;
		lightBuffer[0].hideFlags = HideFlags.DontSave;
        lightBuffer[0].Create();
		lightBuffer[1] = new RenderTexture(textureSize, textureSize, 24, textureFormat, RenderTextureReadWrite.Linear);
		lightBuffer[1].filterMode = FilterMode.Bilinear;
        lightBuffer[1].enableRandomWrite = true;
		lightBuffer[1].hideFlags = HideFlags.DontSave;
        lightBuffer[1].Create();
        Shader.SetGlobalTexture("LightTex", lightBuffer[0]);
        Shader.SetGlobalVector("LightTexScale", new Vector4(1.0f / size, 1.0f / size, size, size));

        radiosityShader = Resources.Load("Radiosity") as ComputeShader;
        clearKernel = radiosityShader.FindKernel("CSClear");
        sweepKernel = radiosityShader.FindKernel("CSSweep");

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
	}

	private void LateUpdate() {
        if (order == null || order.Length != (sweeps / 4)) 
        {
            order = new int[sweeps / 4];
            for (int i = 0; i < sweeps / 4; ++i) 
            {
                order[i] = i;
            }
            Shuffle(order);
        }

		RenderTexture shapes = RenderTexture.GetTemporary(textureSize, textureSize, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear, 1);
		shapes.filterMode = FilterMode.Point;
		cam.targetTexture = shapes;
		cam.Render();
		cam.targetTexture = null;

        radiosityShader.SetTexture(clearKernel, "LightBufferR", lightBuffer[0]);
        radiosityShader.SetTexture(clearKernel, "LightBufferW", lightBuffer[1]);
        radiosityShader.SetInt("TexSize", textureSize);
        radiosityShader.Dispatch(clearKernel, textureSize / 8, textureSize / 8, 1);

        Swap(lightBuffer);

        radiosityShader.SetTexture(sweepKernel, "ShapesTexR", shapes);
        radiosityShader.SetInt("TexSize", textureSize);
        radiosityShader.SetFloat("FeedbackMultiplier", feedback);
        radiosityShader.SetFloat("Multiplier", multiplier / ((sweepsPerFrame / 4) * 4));
        int linesInFrame = 0;
        for (int k = 0; k < sweepsPerFrame / 4; ++k)
        {
            float rndAngle = Random.Range(-0.5f, 0.5f);
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
                float angle = 0.5f * Mathf.PI * ((order[sweep] + 0.5f + rndAngle) / (sweeps / 4) + q);
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
                radiosityShader.SetTexture(sweepKernel, "LightBufferR", lightBuffer[0]);
                radiosityShader.SetTexture(sweepKernel, "LightBufferW", lightBuffer[1]);
                radiosityShader.SetVector("SweepStart", start);
                radiosityShader.SetVector("SweepStep", dir);
                radiosityShader.Dispatch(sweepKernel, lines / 8, 1, 1);
                Swap(lightBuffer);

                linesInFrame += (lines / 8) * 8;
            }
            sweep = (sweep + 1) % (sweeps / 4);
        }

        //Graphics.Blit(shapes, lightTexture);
        RenderTexture.ReleaseTemporary(shapes);
        Shader.SetGlobalTexture("LightTex", lightBuffer[0]);

        avgLPS = Mathf.Lerp(avgLPS, linesInFrame / Time.deltaTime, 0.01f);
	}

	private void OnDisable() {
		DestroyImmediate(cam);
        Release(lightBuffer);
	}

    void OnGUI()
    {
        var text = "Avg LPS: " + string.Format("{0:F1}M", avgLPS / 1000000);
        GUI.Label(new Rect(0, 0, 200, 200), text);
    }

    public static void Shuffle(int[] array)
    {
        int n = array.Length;
        while (n > 1) 
        {
            int k = Random.Range(0, n--);
            int temp = array[n];
            array[n] = array[k];
            array[k] = temp;
        }
    }

    public static void Swap(RenderTexture[] buffers)
    {
        RenderTexture tmp = buffers[0];
        buffers[0] = buffers[1];
        buffers[1] = tmp;
    }

    public static void Release(IList<RenderTexture> buffers)
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
