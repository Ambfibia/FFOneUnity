using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Tutorial;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.Content
{
    /// <summary>GameFrame.LoadVehicle's persistent effects on exact tag01–tag04 model sockets.</summary>
    public sealed class NativeVehicleExhaust : MonoBehaviour
    {
        readonly List<TutorialEffectAssets> definitions = new();
        readonly List<Material> owned = new();
        public int TrailCount { get; private set; }
        public string Style { get; private set; }
        internal void Initialize(string modelPath)
        {
            Style = modelPath.Split('/').Contains("vehicle_hoverboard") ? "board" :
                modelPath.Split('/').Any(p => p is "vehicle_cloud" or "vehicle_hoverboard2") ? "cloud" : "standard";
            var catalog = NativeData.Object("effects/vehicles/catalog");
            if ((string)catalog["schema"] != "ffone.vehicle-trails.v1") throw new InvalidDataException("Unsupported vehicle exhaust catalog.");
            var plan = catalog["trails"].Single(t => (string)t["name"] == Style);
            foreach (var socket in GetComponentsInChildren<Transform>(true).Where(t => t.name is "tag01" or "tag02" or "tag03" or "tag04").ToArray())
            {
                var material = new Material(ContentAssets.Load<Shader>("Tutorial/Particle")) { name = "Vehicle " + Style + " exhaust", renderQueue = 3000 };
                owned.Add(material);
                string texturePath = (string)plan["texture"][0];
                material.mainTexture = ContentAssets.Load<Texture2D>(texturePath.Substring(0, texturePath.LastIndexOf('.')));
                if (!material.mainTexture) throw new FileNotFoundException("Missing accepted vehicle exhaust texture: " + texturePath);
                var tint = plan["material_tint"];
                material.SetColor("_TintColor", new Color((float)tint[0], (float)tint[1], (float)tint[2], (float)tint[3]));
                material.SetFloat("_SrcBlend", 1);
                var definition = Style == "standard" ? TutorialEffectAssets.Load(15, TutorialCinematicEffects.ModelPath(15)) :
                    new TutorialEffectAssets { Id = Style == "board" ? 835 : 836, Objects = new JArray() };
                definitions.Add(definition);
                definition.TrailLength = (int)plan["length"]; definition.TrailHeight = (float)plan["height"];
                definition.TrailStep = (float)plan["interpolation_step"]; definition.TrailMaterial = material;
                var effect = new GameObject("Vehicle exhaust " + Style + " " + socket.name);
                effect.transform.SetPositionAndRotation(socket.position, socket.rotation);
                effect.transform.SetParent(socket, true);
                effect.AddComponent<TutorialEffectPlayback>().Initialize(definition, 1, Camera.main);
                foreach (var node in effect.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = gameObject.layer;
                TrailCount++;
            }
        }
        void OnDestroy()
        {
            foreach (var definition in definitions) definition.Dispose(); definitions.Clear();
            foreach (var material in owned) TutorialEffectAssets.Release(material); owned.Clear();
        }
    }
}
