using System.IO;
using UnityEngine;

public class FrameRecorder : MonoBehaviour
{
    /// <summary>
    ///
    ///     Test helper: renders a camera that follows the walker to numbered PNGs at a fixed frame rate
    ///     (Time.captureFramerate, so game time advances exactly 1/fps per frame however slow the editor is).
    ///     Turn the PNGs into a video with ffmpeg.
    ///
    /// </summary>



    [SerializeField] private WalkerController walker;
    [SerializeField] private WalkerDemoDriver driver;
    [SerializeField] private Camera recordCamera;
    [Tooltip("Absolute folder for the frames, outside Assets")]
    [SerializeField] private string outputFolder = "/tmp/walker_frames";
    [SerializeField] private int width = 1280;
    [SerializeField] private int height = 720;
    [SerializeField] private int fps = 30;
    [Tooltip("Camera position relative to the walker's heading frame (x right, y up, z forward)")]
    [SerializeField] private Vector3 cameraOffset = new(55f, 22f, -40f);
    [SerializeField] private float maxSeconds = 40f;

    private RenderTexture _target;
    private Texture2D _readback;
    private int _frame;
    private Vector3 _smoothedFocus;


    private void Start()
    {
        Directory.CreateDirectory(outputFolder);
        foreach (string f in Directory.GetFiles(outputFolder, "frame_*.png")) File.Delete(f);
        Time.captureFramerate = fps;
        _target = new RenderTexture(width, height, 24);
        _readback = new Texture2D(width, height, TextureFormat.RGB24, false);
    }

    private void LateUpdate()
    {
        Transform body = walker != null ? walker.Body : null;
        if (body == null || recordCamera == null) return;
        if (_frame / (float)fps > maxSeconds || (driver != null && driver.Finished)) { enabled = false; return; }

        // Follow the walker from the side, turning with its heading but not its roll or pitch.
        Vector3 focus = body.position;
        _smoothedFocus = _frame == 0 ? focus : Vector3.Lerp(_smoothedFocus, focus, 0.15f);
        Vector3 forward = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
        Quaternion heading = Quaternion.LookRotation(forward, Vector3.up);
        recordCamera.transform.position = _smoothedFocus + heading * cameraOffset;
        recordCamera.transform.LookAt(_smoothedFocus + Vector3.up * 2f);

        recordCamera.targetTexture = _target;
        recordCamera.Render();
        RenderTexture.active = _target;
        _readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        _readback.Apply();
        RenderTexture.active = null;
        recordCamera.targetTexture = null;
        File.WriteAllBytes(Path.Combine(outputFolder, $"frame_{_frame:D5}.png"), _readback.EncodeToPNG());
        _frame++;
    }
}
