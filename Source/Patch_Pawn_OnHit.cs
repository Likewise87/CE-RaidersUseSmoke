// File: Patch_Pawn_OnHit.cs
// Project: TSA.CE.RaidersUseSmoke (Odyssey 1.6 only)

using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;
using UnityEngine;
using CombatExtended;

namespace TSA.CE.SmokeUnderFire
{
    [StaticConstructorOnStartup]
    public static class HitSmokePatcher
    {
        static HitSmokePatcher()
        {
            new Harmony("TSA.CE.RaidersUseSmoke.Minimal").PatchAll();
        }
    }

    /// <summary>
    /// Throw smoke on hit (Odyssey 1.6):
    /// - PostApplyDamage only; skip if downed.
    /// - Needs a smoke grenade in inventory; consumes 1 after a successful launch.
    /// - Launches TSA_Proj_GrenadeSmoke (mod-owned; settings radius does not touch CE).
    /// </summary>
    public static class Patch_Pawn_OnHit_Min
    {
        private const string ModProjDefName = "TSA_Proj_GrenadeSmoke";
        private const string CeWeaponDefName = "CE_Weapon_GrenadeSmoke";
        private const string GrenadeSmokeTag = "GrenadeSmoke";

        private static SmokeUnderFireSettings S => SmokeUnderFireMod.Settings ?? new SmokeUnderFireSettings();

        private static readonly Dictionary<int, int> _lastProcTick = new();
        private static readonly Dictionary<int, int> _nextAllowedTick = new();

        private static ThingDef _cachedProjDef;
        private static bool _projLookupDone;

        private static bool TryProc(Pawn p, DamageInfo dinfo)
        {
            if (p == null || !p.Spawned || p.Dead || p.Downed) return false;
            if (p.Faction == null || !p.HostileTo(Faction.OfPlayer)) return false;

            int now = Find.TickManager.TicksGame;

            if (_lastProcTick.TryGetValue(p.thingIDNumber, out var last) && last == now)
                return false;

            if (_nextAllowedTick.TryGetValue(p.thingIDNumber, out var next) && now < next)
                return false;

            var grenade = FindSmokeGrenadeInInventory(p);
            if (grenade == null) return false;

            IntVec3 target;
            if (dinfo.Instigator != null && dinfo.Instigator.Spawned)
                target = MidpointClamped(p, dinfo.Instigator.Position, 10);
            else
                target = FallbackForwardCellClamped(p, 10);

            if (!LaunchSmokeProjectile(p, target))
                return false;

            ConsumeOne(grenade, p);
            PlayThrowSound(p);

            _lastProcTick[p.thingIDNumber] = now;
            _nextAllowedTick[p.thingIDNumber] = now + S.CooldownTicks;

            if (S.debugLogs)
                Log.Message($"[RaidersUseSmoke] {p.LabelShortCap} used smoke. Next allowed at tick {_nextAllowedTick[p.thingIDNumber]}.");

            return true;
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
        public static class PostApplyDamage_Patch
        {
            public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
            {
                try { TryProc(__instance, dinfo); }
                catch (Exception e) { Log.Error($"[RaidersUseSmoke] PostApplyDamage error: {e}"); }
            }
        }

        private static Thing FindSmokeGrenadeInInventory(Pawn p)
        {
            var inv = p.inventory?.innerContainer;
            if (inv == null) return null;

            foreach (var t in inv)
            {
                if (t?.def == null) continue;
                if (t.def.modExtensions != null)
                {
                    for (int i = 0; i < t.def.modExtensions.Count; i++)
                    {
                        if (t.def.modExtensions[i] is SmokeGrenadeTag)
                            return t;
                    }
                }
            }

            foreach (var t in inv)
            {
                if (t?.def == null) continue;
                if (t.def.defName == CeWeaponDefName)
                    return t;
                if (t.def.weaponTags != null && t.def.weaponTags.Contains(GrenadeSmokeTag))
                    return t;
            }

            return null;
        }

        private static void ConsumeOne(Thing grenade, Pawn owner)
        {
            if (grenade == null) return;
            var container = owner.inventory?.innerContainer;

            if (grenade.stackCount > 1)
            {
                var one = grenade.SplitOff(1);
                one.Destroy(DestroyMode.Vanish);
            }
            else
            {
                if (container != null && container.Contains(grenade)) container.Remove(grenade);
                grenade.Destroy(DestroyMode.Vanish);
            }
        }

        private static void PlayThrowSound(Pawn p)
        {
            var s = DefDatabase<SoundDef>.GetNamedSilentFail("ThrowGrenade")
                    ?? DefDatabase<SoundDef>.GetNamedSilentFail("Interact_Grenade");
            if (s == null) return;
            SoundStarter.PlayOneShot(s, SoundInfo.InMap(new TargetInfo(p.Position, p.Map)));
        }

        private static ThingDef ResolveModSmokeProjectileDef()
        {
            if (_projLookupDone)
                return _cachedProjDef;

            _projLookupDone = true;
            _cachedProjDef = DefDatabase<ThingDef>.GetNamedSilentFail(ModProjDefName);
            if (_cachedProjDef == null && S.debugLogs)
                Log.Warning($"[RaidersUseSmoke] Missing ThingDef '{ModProjDefName}'.");
            return _cachedProjDef;
        }

        private static bool LaunchSmokeProjectile(Pawn launcher, IntVec3 targetCell)
        {
            var projDef = ResolveModSmokeProjectileDef();
            if (projDef == null)
                return false;

            // Mutate only the mod-owned def — never CE_Proj_GrenadeSmoke.
            if (projDef.projectile is ProjectilePropertiesCE ceProps)
                ceProps.explosionRadius = Mathf.Clamp(S.smokeRadius, 1f, 8f);
            else if (projDef.projectile != null)
                projDef.projectile.explosionRadius = Mathf.Clamp(S.smokeRadius, 1f, 8f);

            if (S.debugLogs)
                Log.Message($"[RaidersUseSmoke] Launch '{projDef.defName}' radius {S.smokeRadius:0.##} at {targetCell} (launcher={launcher.LabelShortCap})");

            // CE projectiles are ProjectileCE (ThingWithComps), not Verse.Projectile —
            // must launch via CE_Utility, not vanilla Projectile.Launch.
            Vector3 src = launcher.DrawPos;
            Vector2 origin = new Vector2(src.x, src.z);
            Vector3 dst = targetCell.ToVector3Shifted();
            Vector2 dest = new Vector2(dst.x, dst.z);
            Vector2 delta = dest - origin;

            float shotSpeed = projDef.projectile?.speed ?? 12f;
            float range = delta.magnitude;
            const float shotHeight = 1f;
            // Must use GravityPerWidth (same as CE Verb/TrajectoryWorker), not GravityConst (9.8) —
            // Destination = DistanceTraveled(..., GravityPerWidth); mismatch overshoots badly.
            float shotAngle = 0f;
            if (range >= 0.01f)
            {
                if (projDef.projectile is ProjectilePropertiesCE ceProj)
                {
                    var source = new Vector3(src.x, shotHeight, src.z);
                    var targetPos = new Vector3(dst.x, 0f, dst.z);
                    shotAngle = ceProj.TrajectoryWorker.ShotAngle(ceProj, source, targetPos, shotSpeed);
                }
                else
                {
                    shotAngle = CE_Utility.GetShotAngle(
                        shotSpeed, range, -shotHeight, false, CE_Utility.GravityConst);
                }
            }
            // CE Destination = origin + Vector2.up.RotatedBy(shotRotation); needs negated AngleFlat.
            float shotRotation = -Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;

            CE_Utility.LaunchProjectileCE(
                projDef,
                origin,
                new LocalTargetInfo(targetCell),
                launcher,
                shotAngle,
                shotRotation,
                shotHeight,
                shotSpeed);
            return true;
        }

        private static IntVec3 MidpointClamped(Pawn pawn, IntVec3 instPos, int maxFromPawn)
        {
            var map = pawn.Map;
            var mid = MidpointCell(map, pawn.Position, instPos);

            var from = pawn.Position.ToVector3Shifted();
            var to = mid.ToVector3Shifted();
            var dir = (to - from);
            float dist = dir.MagnitudeHorizontal();
            if (dist <= maxFromPawn) return mid;

            var clamped = from + dir.normalized * maxFromPawn;
            var c = IntVec3.FromVector3(clamped);
            if (!c.InBounds(map) || !c.Standable(map))
            {
                if (TryFindNearbyStandable(map, c, 2f, out var alt))
                    return alt;
                return pawn.Position;
            }
            return c;
        }

        private static IntVec3 FallbackForwardCellClamped(Pawn p, int maxFromPawn)
        {
            var map = p.Map;

            if (p.pather?.Moving == true)
            {
                IntVec3 dest = p.pather.Destination.Cell;
                var mid = MidpointCell(map, p.Position, dest);
                return MidpointClamped(p, mid, maxFromPawn);
            }

            var forward = p.Rotation.FacingCell;
            var ahead = p.Position + forward * Mathf.Min(2, maxFromPawn);
            if (ahead.InBounds(map) && ahead.Standable(map)) return ahead;

            return p.Position;
        }

        private static IntVec3 MidpointCell(Map map, IntVec3 a, IntVec3 b)
        {
            var mid = IntVec3.FromVector3((a.ToVector3Shifted() + b.ToVector3Shifted()) * 0.5f);
            if (!mid.InBounds(map) || !mid.Standable(map))
            {
                if (TryFindNearbyStandable(map, mid, 2f, out var alt))
                    return alt;
                return a;
            }
            return mid;
        }

        /// <summary>
        /// IntVec3.FirstOrDefault returns (0,0,0) on empty, which is IsValid — do not use it.
        /// </summary>
        private static bool TryFindNearbyStandable(Map map, IntVec3 center, float radius, out IntVec3 cell)
        {
            foreach (var x in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (x.InBounds(map) && x.Standable(map))
                {
                    cell = x;
                    return true;
                }
            }
            cell = IntVec3.Invalid;
            return false;
        }
    }

    /// <summary>Optional marker you can add to smoke grenade ThingDefs.</summary>
    public class SmokeGrenadeTag : DefModExtension { }
}
