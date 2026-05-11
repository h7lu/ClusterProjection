using System;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace ClusterProjection;

public class CP_CompRefuelable : CompRefuelable
{
    private static readonly FieldInfo ConfiguredTargetFuelLevelField = typeof(CompRefuelable).GetField("configuredTargetFuelLevel", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo FuelField = typeof(CompRefuelable).GetField("fuel", BindingFlags.Instance | BindingFlags.NonPublic);

    private CompProperties_Refuelable mutableProps;
    private float baseFuelCapacity = -1f;
    private float baseInitialConfigurableTargetFuelLevel = -1f;

    public float BaseFuelCapacity => baseFuelCapacity >= 0f ? baseFuelCapacity : Props.fuelCapacity;

    public override void Initialize(CompProperties props)
    {
        base.Initialize(props);
        var refuelableProps = (CompProperties_Refuelable)props;
        baseFuelCapacity = refuelableProps.fuelCapacity;
        baseInitialConfigurableTargetFuelLevel = refuelableProps.initialConfigurableTargetFuelLevel;
    }

    public void SetCapacity(float requestedCapacity)
    {
        if (baseFuelCapacity < 0f)
            baseFuelCapacity = Props.fuelCapacity;
        if (baseInitialConfigurableTargetFuelLevel < 0f)
            baseInitialConfigurableTargetFuelLevel = Props.initialConfigurableTargetFuelLevel;

        EnsureMutableProps();

        var effectiveCapacity = Mathf.Max(requestedCapacity, BaseFuelCapacity);
        mutableProps.fuelCapacity = effectiveCapacity;
        if (mutableProps.targetFuelLevelConfigurable)
            mutableProps.initialConfigurableTargetFuelLevel = effectiveCapacity;

        var configuredTargetFuelLevel = GetConfiguredTargetFuelLevel();
        if (configuredTargetFuelLevel >= 0f && configuredTargetFuelLevel > effectiveCapacity)
            ConfiguredTargetFuelLevelField?.SetValue(this, effectiveCapacity);
    }

    public float ClampFuelToCapacity()
    {
        if (Fuel <= Props.fuelCapacity)
            return 0f;

        var overflow = Fuel - Props.fuelCapacity;
        FuelField?.SetValue(this, Props.fuelCapacity);
        return overflow;
    }

    private void EnsureMutableProps()
    {
        if (mutableProps != null)
            return;

        mutableProps = CloneProps(Props);
        props = mutableProps;
    }

    private float GetConfiguredTargetFuelLevel()
    {
        if (ConfiguredTargetFuelLevelField?.GetValue(this) is float configuredTargetFuelLevel)
            return configuredTargetFuelLevel;

        return -1f;
    }

    private static CompProperties_Refuelable CloneProps(CompProperties_Refuelable source)
    {
        var clone = (CompProperties_Refuelable)Activator.CreateInstance(source.GetType());
        for (var type = source.GetType(); type != null; type = type.BaseType)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (var i = 0; i < fields.Length; i++)
                fields[i].SetValue(clone, fields[i].GetValue(source));
        }

        return clone;
    }
}