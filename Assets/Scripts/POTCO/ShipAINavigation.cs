using UnityEngine;

namespace POTCO
{
    public readonly struct ShipAIObstacleThreat
    {
        public ShipAIObstacleThreat(Vector3 direction, float weight, bool emergency)
        {
            Direction = direction;
            Weight = Mathf.Clamp01(weight);
            Emergency = emergency;
        }

        public Vector3 Direction { get; }
        public float Weight { get; }
        public bool Emergency { get; }
    }

    public static class ShipAINavigation
    {
        private const float DirectionEpsilon = 0.0001f;
        private const float FrontQuarterSideDot = 0.45f;

        public static bool TryGetBroadsideFireSide(
            Vector3 shipPosition,
            Quaternion shipRotation,
            Vector3 targetPosition,
            float fireArcDegrees,
            float minRange,
            float maxRange,
            out bool fireLeftSide)
        {
            fireLeftSide = false;

            Vector3 toTarget = Flatten(targetPosition - shipPosition);
            float distance = toTarget.magnitude;
            if (distance < Mathf.Max(0f, minRange) || distance > Mathf.Max(minRange, maxRange))
            {
                return false;
            }

            Vector3 targetDirection = toTarget / distance;
            Vector3 bowDirection = Flatten(-(shipRotation * Vector3.forward));
            if (bowDirection.sqrMagnitude <= DirectionEpsilon)
            {
                return false;
            }

            float angleFromBow = Vector3.Angle(bowDirection.normalized, targetDirection);
            float arc = Mathf.Clamp(fireArcDegrees, 0f, 90f);
            if (Mathf.Abs(angleFromBow - 90f) > arc)
            {
                return false;
            }

            Vector3 right = Flatten(shipRotation * Vector3.right).normalized;
            fireLeftSide = Vector3.Dot(targetDirection, right) > 0f;
            return true;
        }

        public static Vector3 GetBroadsideOrbitDestination(
            Vector3 shipPosition,
            Vector3 targetPosition,
            float orbitRadius,
            bool clockwise,
            float tangentLead)
        {
            Vector3 radial = Flatten(shipPosition - targetPosition);
            if (radial.sqrMagnitude <= DirectionEpsilon)
            {
                radial = Vector3.right;
            }

            radial.Normalize();
            Vector3 tangent = clockwise
                ? Vector3.Cross(Vector3.up, radial)
                : Vector3.Cross(radial, Vector3.up);
            tangent = Flatten(tangent).normalized;

            Vector3 destination = targetPosition + radial * Mathf.Max(0f, orbitRadius) + tangent * Mathf.Max(0f, tangentLead);
            destination.y = shipPosition.y;
            return destination;
        }

        public static Vector3 BlendAvoidance(
            Vector3 preferredDirection,
            Vector3 fallbackSideDirection,
            ShipAIObstacleThreat[] threats)
        {
            Vector3 preferred = NormalizeOrFallback(preferredDirection, Vector3.forward);
            Vector3 fallbackSide = NormalizeOrFallback(fallbackSideDirection, Vector3.right);
            Vector3 avoidance = Vector3.zero;
            float totalWeight = 0f;
            bool hasCenteredEmergency = false;

            if (threats != null)
            {
                for (int i = 0; i < threats.Length; i++)
                {
                    ShipAIObstacleThreat threat = threats[i];
                    if (threat.Weight <= 0f)
                    {
                        continue;
                    }

                    Vector3 obstacleDirection = Flatten(threat.Direction);
                    if (obstacleDirection.sqrMagnitude <= DirectionEpsilon)
                    {
                        continue;
                    }

                    obstacleDirection.Normalize();
                    float forwardDot = Mathf.Clamp01(Vector3.Dot(preferred, obstacleDirection));
                    if (forwardDot <= 0f && !threat.Emergency)
                    {
                        continue;
                    }

                    float sideDot = Vector3.Dot(obstacleDirection, fallbackSide);
                    Vector3 awayDirection;
                    if (Mathf.Abs(sideDot) < FrontQuarterSideDot)
                    {
                        awayDirection = fallbackSide;
                        hasCenteredEmergency |= threat.Emergency;
                    }
                    else
                    {
                        awayDirection = sideDot > 0f ? -fallbackSide : fallbackSide;
                    }

                    float weightedThreat = threat.Weight * Mathf.Lerp(0.4f, 1f, forwardDot);
                    if (threat.Emergency)
                    {
                        weightedThreat *= 1.5f;
                    }

                    avoidance += awayDirection * weightedThreat;
                    totalWeight += weightedThreat;
                }
            }

            if (totalWeight <= 0f)
            {
                return preferred;
            }

            float blend = Mathf.Clamp01(totalWeight);
            Vector3 result = Vector3.Lerp(preferred, avoidance.normalized, blend);
            if (hasCenteredEmergency)
            {
                result += preferred * 0.35f;
            }

            return NormalizeOrFallback(result, preferred);
        }

        public static bool ShouldMaintainAvoidance(
            bool currentlyAvoiding,
            float threatWeight,
            float enterThreshold,
            float exitThreshold)
        {
            float enter = Mathf.Clamp01(enterThreshold);
            float exit = Mathf.Clamp(exitThreshold, 0f, enter);
            float threat = Mathf.Clamp01(threatWeight);

            if (threat >= enter)
            {
                return true;
            }

            if (threat <= exit)
            {
                return false;
            }

            return currentlyAvoiding;
        }

        public static float GetAdaptiveAvoidanceScanInterval(
            float baseInterval,
            bool currentlyAvoiding,
            float threatWeight,
            float urgentThreshold)
        {
            float interval = Mathf.Max(0.02f, baseInterval);
            float threat = Mathf.Clamp01(threatWeight);
            float urgent = Mathf.Clamp01(urgentThreshold);
            if (threat >= urgent)
            {
                return interval;
            }

            if (currentlyAvoiding)
            {
                return interval * 1.5f;
            }

            return Mathf.Max(interval, 0.35f);
        }

        public static bool ShouldRunClearanceCandidateSearch(float threatWeight, float urgentThreshold)
        {
            return Mathf.Clamp01(threatWeight) >= Mathf.Clamp01(urgentThreshold);
        }

        public static Vector3 BlendCachedAvoidance(
            Vector3 preferredDirection,
            Vector3 cachedAvoidanceDirection,
            float avoidanceWeight)
        {
            Vector3 preferred = NormalizeOrFallback(preferredDirection, Vector3.forward);
            Vector3 avoidance = Flatten(cachedAvoidanceDirection);
            if (avoidanceWeight <= 0f || avoidance.sqrMagnitude <= DirectionEpsilon)
            {
                return preferred;
            }

            float blend = Mathf.Clamp01(avoidanceWeight * 0.7f);
            return NormalizeOrFallback(Vector3.Lerp(preferred, avoidance.normalized, blend), preferred);
        }

        public static Vector3 StabilizeHeading(
            Vector3 currentDirection,
            Vector3 desiredDirection,
            float deadbandDegrees,
            float response)
        {
            Vector3 desired = NormalizeOrFallback(desiredDirection, Vector3.forward);
            Vector3 current = NormalizeOrFallback(currentDirection, desired);
            float angle = Vector3.Angle(current, desired);
            if (angle <= Mathf.Max(0f, deadbandDegrees))
            {
                return current;
            }

            float blend = Mathf.Clamp01(response);
            if (blend >= 1f)
            {
                return desired;
            }

            return NormalizeOrFallback(Vector3.Slerp(current, desired, blend), desired);
        }

        public static int ExcludeLayersFromMask(int mask, params int[] layerIndices)
        {
            if (layerIndices == null)
            {
                return mask;
            }

            int result = mask;
            for (int i = 0; i < layerIndices.Length; i++)
            {
                int layer = layerIndices[i];
                if (layer < 0 || layer > 31)
                {
                    continue;
                }

                result &= ~(1 << layer);
            }

            return result;
        }

        public static bool ShouldReturnToSpawn(
            float targetDistance,
            float detectionRange,
            float targetLostDistancePadding,
            float distanceFromSpawn,
            float maxChaseDistance)
        {
            if (maxChaseDistance > 0f && distanceFromSpawn > maxChaseDistance)
            {
                return true;
            }

            float lostDistance = Mathf.Max(0f, detectionRange) + Mathf.Max(0f, targetLostDistancePadding);
            return targetDistance > lostDistance;
        }

        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            value = Flatten(value);
            if (value.sqrMagnitude > DirectionEpsilon)
            {
                return value.normalized;
            }

            fallback = Flatten(fallback);
            if (fallback.sqrMagnitude > DirectionEpsilon)
            {
                return fallback.normalized;
            }

            return Vector3.forward;
        }
    }
}
