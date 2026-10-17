using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FFOne.World
{
    public sealed class WorldViewer : MonoBehaviour
    {
        WorldStreamer stream;
        float yaw = 135, pitch, frames, elapsed, fps;
        int tile;
        void Start()
        {
            stream = GetComponent<WorldStreamer>();
            if (Environment.GetCommandLineArgs().Contains("--world-boundary-smoke")) { enabled=false; return; }
            if (Environment.GetCommandLineArgs().Contains("--world-benchmark") || (Environment.GetCommandLineArgs().Contains("--world-district-smoke") || Environment.GetCommandLineArgs().Contains("--whole-world-smoke"))) return;
            stream.Anchor = new Vector3(6374.3188f, -56.4746f, 668.2039f);
            PlaceCamera();
        }
        void Update()
        {
            if (!stream || GetComponent<WorldBenchmark>()?.Running == true || (Environment.GetCommandLineArgs().Contains("--world-district-smoke") || Environment.GetCommandLineArgs().Contains("--whole-world-smoke"))) return;
            var keys = Keyboard.current; var mouse = Mouse.current;
            if (keys != null)
            {
                var direction = new Vector3((keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0),
                    (keys.eKey.isPressed ? 1 : 0) - (keys.qKey.isPressed ? 1 : 0),
                    (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0));
                float speed = keys.leftShiftKey.isPressed ? 100 : 25;
                stream.Anchor += Quaternion.Euler(0, yaw, 0) * direction * (Time.unscaledDeltaTime * speed);
                if (keys.f8Key.wasPressedThisFrame && stream.Catalog)
                { var target = stream.Catalog.Tiles[tile++ % stream.Catalog.Tiles.Length]; stream.Anchor = (target.Minimum + target.Maximum) * .5f; stream.Anchor.y = 80; }
            }
            if (mouse != null && mouse.rightButton.isPressed)
            { yaw += mouse.delta.ReadValue().x * .2f; pitch = Mathf.Clamp(pitch - mouse.delta.ReadValue().y * .2f, -80, 80); }
            PlaceCamera(); frames++; elapsed += Time.unscaledDeltaTime;
            if (elapsed >= .5f) { fps = frames / elapsed; frames = elapsed = 0; }
        }
        void PlaceCamera()
        {
            if (!stream.View) return;
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            stream.View.transform.SetPositionAndRotation(stream.Anchor + Vector3.up * 1.4f - rotation * Vector3.forward * 4, rotation);
        }
        void OnGUI()
        {
            if (!stream || GetComponent<WorldBenchmark>()?.Running == true) return;
            GUI.Box(new Rect(12, 12, 550, 95), $"Native world — {fps:F1} FPS | tiles {stream.ResidentTiles}/9 | pending {stream.PendingTiles}\n" +
                $"WASD move · Q/E height · Shift speed · RMB look · F8 next tile\nNative position {-stream.Anchor.x:F1} {stream.Anchor.y:F1} {stream.Anchor.z:F1}\n{stream.LastError}");
        }
    }
}
