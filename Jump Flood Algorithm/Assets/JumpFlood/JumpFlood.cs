using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class JumpFlood : MonoBehaviour
{
    public bool isGpu;
    public int resolution; // square first later variation
    public ComputeShader shader;
    public Camera RenderPlayerCamera;

    //CPU
    private Texture2D texture_cpu;
    private Dictionary<Color, Vector2Int> seedInfoDictionary = new Dictionary<Color, Vector2Int>();

    //GPU
    private RenderTexture sceneTexture;
    private RenderTexture seedTexture;
    private RenderTexture seedColorTexture;
    private RenderTexture outputTexture;


    private void Start()
    {
        if (isGpu)
        {
            InitializeGPUTexture();
            StartCoroutine(GPUMethod());
        }
        else
        {
            InitializeCpuTexture();
            StartCoroutine(CPUMethod());
        }

    }

    public void InitializeCpuTexture()
    {
        texture_cpu = new Texture2D(resolution, resolution, TextureFormat.ARGB32, false);
        texture_cpu.wrapMode = TextureWrapMode.Clamp;
        texture_cpu.filterMode = FilterMode.Point;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                texture_cpu.SetPixel(x, y, Color.black);
            }
        }

        texture_cpu.SetPixel(0, 0, Color.red);
        texture_cpu.SetPixel(resolution - 1, 0, Color.yellow);
        texture_cpu.SetPixel(0, resolution - 1, Color.green);
        texture_cpu.SetPixel(resolution - 1, resolution - 1, Color.blue);

        seedInfoDictionary.Add(Color.red, Vector2Int.zero);
        seedInfoDictionary.Add(Color.yellow, new Vector2Int(resolution - 1, 0));
        seedInfoDictionary.Add(Color.green, new Vector2Int(0, resolution - 1));
        seedInfoDictionary.Add(Color.blue, new Vector2Int(resolution - 1, resolution - 1));

        texture_cpu.Apply();
    }

    public void InitializeGPUTexture()
    {
        sceneTexture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat);
        sceneTexture.wrapMode = TextureWrapMode.Clamp;
        sceneTexture.filterMode = FilterMode.Bilinear;
        sceneTexture.enableRandomWrite = true;

        seedTexture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat);
        seedTexture.wrapMode = TextureWrapMode.Clamp;
        seedTexture.filterMode = FilterMode.Bilinear;
        seedTexture.enableRandomWrite = true;

        seedColorTexture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat);
        seedColorTexture.wrapMode = TextureWrapMode.Clamp;
        seedColorTexture.filterMode = FilterMode.Bilinear;
        seedColorTexture.enableRandomWrite = true;

        outputTexture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat);
        outputTexture.wrapMode = TextureWrapMode.Clamp;
        outputTexture.filterMode = FilterMode.Bilinear;
        outputTexture.enableRandomWrite = true;

        seedTexture.Create();
        seedColorTexture.Create();
        outputTexture.Create();
        sceneTexture.Create();

        RenderPlayerCamera.targetTexture = sceneTexture;
    }

    public IEnumerator CPUMethod()
    {
        int length = 1;

        bool test = true;

        while (test)
        {
            int step = resolution / (int)Mathf.Pow(2, length);

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Color curPixColor = texture_cpu.GetPixel(x, y);

                    if (curPixColor == Color.black)
                        continue;

                    for (int j = -1; j <= 1; j++)
                    {
                        for (int i = -1; i <= 1; i++)
                        {
                            Vector2Int neighborPixel = new Vector2Int(x + i * step, y + j * step);
                            Color neighborColor = texture_cpu.GetPixel(neighborPixel.x, neighborPixel.y);
                            if (neighborPixel.x < 0 || neighborPixel.y < 0 || neighborPixel.x >= resolution || neighborPixel.y >= resolution || (i == 0 && j == 0))
                                continue;

                            if (neighborColor == Color.black)
                            {
                                texture_cpu.SetPixel(neighborPixel.x, neighborPixel.y, curPixColor);
                            }
                            else
                            {
                                float distNtoNs = Vector2.Distance(neighborPixel, seedInfoDictionary[neighborColor]);
                                float distNtoCs = Vector2.Distance(neighborPixel, seedInfoDictionary[curPixColor]);

                                if (distNtoCs <= distNtoNs)
                                {
                                    texture_cpu.SetPixel(neighborPixel.x, neighborPixel.y, curPixColor);
                                }
                            }
                        }
                    }
                }
            }

            yield return null;

            length++;

            if (step == 1)

                test = false;

        }

        texture_cpu.Apply();

        Shader.SetGlobalTexture("_RT", texture_cpu);
    }

    public IEnumerator GPUMethod()
    {
        yield return new WaitForSeconds(0.5f);

        int KernelId_0 = shader.FindKernel("SetSeed");
        int KernelId_1 = shader.FindKernel("JumpFlood");
        int KernelId_2 = shader.FindKernel("SetColor");
        int KernelId_3 = shader.FindKernel("DistanceTransform");

        shader.GetKernelThreadGroupSizes(KernelId_0, out uint _x, out uint _y, out _);

        int threadX = Mathf.CeilToInt((float)resolution / _x);
        int threadY = Mathf.CeilToInt((float)resolution / _y);

        shader.SetInt("resolution", resolution);

        //Init Seed

        shader.SetTexture(KernelId_0, "InputTexture", sceneTexture); // from texture
        shader.SetTexture(KernelId_0, "SeedTexture", seedTexture); // normal
        shader.SetTexture(KernelId_0, "SeedColorTexture", seedColorTexture);

        shader.Dispatch(KernelId_0, threadX, threadY, 1);

        //Flood

        bool test = true;
        int length = 1;
        while (test)
        {
            int step = resolution / (int)Mathf.Pow(2, length);

            shader.SetTexture(KernelId_1, "SeedTexture", seedTexture);
            shader.SetTexture(KernelId_1, "Result", outputTexture);
            shader.SetInt("_step", step);

            shader.Dispatch(KernelId_1, threadX, threadY, 1);

            Graphics.Blit(outputTexture, seedTexture);

            yield return null;

            length++;

            if (step == 1)
                test = false;
        }

        //Color

        shader.SetTexture(KernelId_2, "SeedTexture", seedTexture);
        shader.SetTexture(KernelId_2, "SeedColorTexture", seedColorTexture);
        shader.SetTexture(KernelId_2, "Result", outputTexture);

        shader.Dispatch(KernelId_2, threadX, threadY, 1);

        //Distance Transform

        //shader.SetTexture(KernelId_3, "SeedTexture", seedTexture);
        //shader.SetTexture(KernelId_3, "Result", outputTexture);

        //shader.Dispatch(KernelId_3, threadX, threadY, 1);

        Shader.SetGlobalTexture("_RT", outputTexture);
    }
}
