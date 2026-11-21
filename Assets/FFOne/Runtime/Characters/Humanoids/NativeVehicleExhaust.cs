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
        public int TrailCount { get; private set; }
        public string Style { get; private set; }
        internal void Initialize(string modelPath)
        {
            Style = modelPath.Split('/').Contains("vehicle_hoverboard") ? "board" :
                modelPath.Split('/').Any(p => p is "vehicle_cloud" or "vehicle_hoverboard2") ? "cloud" : "standard";
            var published=ContentAssets.Load<NativeEffectDefinition>(NativeEffectDefinition.VehicleResource(Style));
            if(!published)throw new InvalidOperationException("Publish native vehicle exhaust "+Style);
            foreach (var socket in GetComponentsInChildren<Transform>(true).Where(t => t.name is "tag01" or "tag02" or "tag03" or "tag04").ToArray())
            {
                var definition=TutorialEffectAssets.FromDefinition(published,defaultModel:true);
                definitions.Add(definition);
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
        }
    }
}
