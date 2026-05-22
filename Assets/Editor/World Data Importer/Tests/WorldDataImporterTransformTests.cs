using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WorldDataImporter.Data;
using WorldDataImporter.Processors;
using Object = UnityEngine.Object;

public class WorldDataImporterTransformTests
{
    private readonly List<GameObject> createdObjects = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject obj in createdObjects)
        {
            if (obj != null)
                Object.DestroyImmediate(obj);
        }

        createdObjects.Clear();
    }

    [Test]
    public void NpcGridPosWorldPlacementKeepsSourceHprWorldSpace()
    {
        GameObject root = Track(new GameObject("root"));
        GameObject parent = Track(new GameObject("1165009442.08Shochet"));
        GameObject npc = Track(new GameObject("1156986248.77jasyeung"));

        parent.transform.SetParent(root.transform, false);
        npc.transform.SetParent(parent.transform, false);

        ImportSettings settings = new ImportSettings { importNPCs = true };
        ObjectData parentData = new ObjectData { id = parent.name, objectType = "Building Exterior" };
        ObjectData npcData = new ObjectData { id = npc.name, objectType = "Townsperson" };

        PropertyProcessor.ProcessProperty("Hpr", "VBase3(98.253, 0.042000, -1.366)", parent, root, true, parentData, null, settings);
        PropertyProcessor.ProcessProperty("Hpr", "VBase3(5.864, 1.198, -0.096)", npc, root, true, npcData, null, settings);

        npcData.gridPos = new Vector3(351.408f, 0.77f, -569.267f);
        npcData.hasPos = false;

        PropertyProcessor.ApplyNpcWorldPlacementFromGridPos(npc, npcData);

        Quaternion expectedWorldRotation = Quaternion.Euler(0.096f, -5.864f, -1.198f);
        Assert.That(Vector3.Distance(npc.transform.position, npcData.gridPos.Value), Is.LessThan(0.001f));
        Assert.That(Quaternion.Angle(npc.transform.rotation, expectedWorldRotation), Is.LessThan(0.1f));
    }

    private GameObject Track(GameObject obj)
    {
        createdObjects.Add(obj);
        return obj;
    }
}
