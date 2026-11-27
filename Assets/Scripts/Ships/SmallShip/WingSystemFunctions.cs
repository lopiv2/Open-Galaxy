using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class WingSystemFunctions
{
    // True when this ship model has open/close wing markers (X-Wing, B-Wing...).
    // Ships without them (TIE, A-Wing) ignore every wing command.
    public static bool HasMovableWings(WingSystems wingSystem)
    {
        if (wingSystem == null)
        {
            return false;
        }

        if (wingSystem.wings == null)
        {
            FindMovableWings(wingSystem);
        }

        return wingSystem.wing01 != null & wingSystem.wing01_open != null & wingSystem.wing01_closed != null;
    }

    // Manual player toggle (V / right stick). Rogue Squadron rule is applied
    // by the callers: closed = faster but weapons blocked. Automatic
    // sequences call OpenWings/CloseWings directly and always win.
    public static void ToggleWingsManual(WingSystems wingSystem)
    {
        if (wingSystem == null || wingSystem.ship == null || wingSystem.ship.isAI == true)
        {
            return;
        }

        if (HasMovableWings(wingSystem) == false)
        {
            return;
        }

        if (Time.time < wingSystem.wingToggleReadyTime)
        {
            return;
        }

        if (wingSystem.wingsOpen == true)
        {
            CloseWings(wingSystem, true);
        }
        else
        {
            OpenWings(wingSystem, true);
        }
    }

    //Rogue Squadron rule: a player ship with movable wings and foils closed
    //cannot fire. AI is never blocked (AI keeps foils open in combat).
    public static bool WeaponsBlockedByWings(Ship ship)
    {
        if (ship == null || ship.isAI == true)
        {
            return false;
        }

        if (ship.wingSystems == null || ship.wingSystems.wingsOpen == true)
        {
            return false;
        }

        return HasMovableWings(ship.wingSystems);
    }

    //Opens the wings
    public static void OpenWings(WingSystems wingSystem, bool manual = false)
    {
        if (wingSystem == null)
        {
            return;
        }

        // The automatic sequences always clear a previous manual override,
        // even when the wings are already in the requested position.
        wingSystem.wingsOverridden = manual;

        if (wingSystem.wingsOpen != true)
        {
            //This searches for movable wings on the ship if they haven't already been loaded
            if (wingSystem.wings == null)
            {
                FindMovableWings(wingSystem);
            }

            //This indicates to other functions whether the wings are open or closed
            wingSystem.wingsOpen = true;
            wingSystem.wingToggleReadyTime = Time.time + 3.5f;

            //Manual toggle: the MissionLog shows a single sticky line, so the
            //open direction must overwrite the "closed" warning explicitly.
            if (manual == true && wingSystem.ship != null && wingSystem.ship.isAI == false)
            {
                HudFunctions.AddToMissionLog("S-FOILS OPEN - WEAPONS ONLINE");
            }

            //This activates the wing rotation
            if (wingSystem.wing01 != null & wingSystem.wing01_open != null & wingSystem.wing01_closed != null)
            {
                Task a = new Task(RotateToWingPosition(wingSystem.wing01, wingSystem.wing01_open.transform, wingSystem.wing01_closed.transform, 3.4f, true));

                //This plays the wings open and close sound
                float spatialBlend = 1f;
                string mixer = "External";

                if (wingSystem.ship.isAI == false)
                {
                    spatialBlend = 0;
                    mixer = "Cockpit";
                }

                AudioFunctions.PlayAudioClip(wingSystem.ship.flightControlSystem_Small.ship.audioManager, "wings_open", mixer, wingSystem.transform.position, spatialBlend, 1, 500, 0.6f);
            }

            if (wingSystem.wing02 != null & wingSystem.wing02_open != null & wingSystem.wing02_closed != null)
            {
                Task a = new Task(RotateToWingPosition(wingSystem.wing02, wingSystem.wing02_open.transform, wingSystem.wing02_closed.transform, 3.4f, true));
            }

            if (wingSystem.wing03 != null & wingSystem.wing03_open != null & wingSystem.wing03_closed != null)
            {
                Task a = new Task(RotateToWingPosition(wingSystem.wing03, wingSystem.wing03_open.transform, wingSystem.wing03_closed.transform, 3.4f, true));
            }

            if (wingSystem.wing04 != null & wingSystem.wing04_open != null & wingSystem.wing04_closed != null)
            {
                Task a = new Task(RotateToWingPosition(wingSystem.wing04, wingSystem.wing04_open.transform, wingSystem.wing04_closed.transform, 3.4f, true));
            }
        }
    }

    //Closes the wings
    public static void CloseWings(WingSystems wingSystems, bool manual = false)
    {
        if (wingSystems != null)
        {
            // The automatic sequences always clear a previous manual override,
            // even when the wings are already in the requested position.
            wingSystems.wingsOverridden = manual;

            if (wingSystems.wingsOpen != false)
            {
                //This searches for movable wings on the ship if they haven't already been loaded
                if (wingSystems.wings == null)
                {
                    FindMovableWings(wingSystems);
                }

                //This indicates to other functions whether the wings are open or closed
                wingSystems.wingsOpen = false;
                wingSystems.wingToggleReadyTime = Time.time + 3.5f;

                //Manual toggle: overwrite the sticky MissionLog line so the
                //HUD reflects the new state immediately.
                if (manual == true && wingSystems.ship != null && wingSystems.ship.isAI == false)
                {
                    HudFunctions.AddToMissionLog("S-FOILS CLOSED - CRUISE MODE (V)");
                }

                //This activates the wing rotation
                if (wingSystems.wing01 != null & wingSystems.wing01_open != null & wingSystems.wing01_closed != null)
                {
                    Task a = new Task(RotateToWingPosition(wingSystems.wing01, wingSystems.wing01_open.transform, wingSystems.wing01_closed.transform, 3.4f, false));

                    //This plays the wings open and close sound
                    float spatialBlend = 1f;
                    string mixer = "External";

                    if (wingSystems.ship.isAI == false)
                    {
                        spatialBlend = 0;
                        mixer = "Cockpit";
                    }

                    AudioFunctions.PlayAudioClip(wingSystems.ship.flightControlSystem_Small.ship.audioManager, "wings_close", mixer, wingSystems.transform.position, spatialBlend, 1, 500, 0.6f);
                }

                if (wingSystems.wing02 != null & wingSystems.wing02_open != null & wingSystems.wing02_closed != null)
                {
                    Task a = new Task(RotateToWingPosition(wingSystems.wing02, wingSystems.wing02_open.transform, wingSystems.wing02_closed.transform, 3.4f, false));
                }

                if (wingSystems.wing03 != null & wingSystems.wing03_open != null & wingSystems.wing03_closed != null)
                {
                    Task a = new Task(RotateToWingPosition(wingSystems.wing03, wingSystems.wing03_open.transform, wingSystems.wing03_closed.transform, 3.4f, false));
                }

                if (wingSystems.wing04 != null & wingSystems.wing04_open != null & wingSystems.wing04_closed != null)
                {
                    Task a = new Task(RotateToWingPosition(wingSystems.wing04, wingSystems.wing04_open.transform, wingSystems.wing04_closed.transform, 3.4f, false));
                }
            }
        }
    }

    //This rotates a wing to the designated position
    public static IEnumerator RotateToWingPosition(GameObject wing, Transform openPosition, Transform closePosition, float speed, bool open)
    {
        Quaternion startRotation = closePosition.localRotation;
        Quaternion endRotation = openPosition.localRotation;

        if (open == false)
        {
            startRotation = openPosition.localRotation;
            endRotation = closePosition.localRotation;
        }

        float timeElapsed = 0;
        float lerpDuration = speed;

        while (timeElapsed < lerpDuration)
        {
            if (wing != null)
            {
                wing.transform.localRotation = Quaternion.Lerp(startRotation, endRotation, timeElapsed / lerpDuration);
            }

            timeElapsed += Time.deltaTime;
            yield return null;
        }

        if (wing != null)
        {
            wing.transform.localRotation = endRotation;
        }
    }

    //Snaps the wing open
    public static void SnapOpenWings(WingSystems wingSystem)
    {
        if (wingSystem == null)
        {
            return;
        }

        wingSystem.wingsOverridden = false;

        if (wingSystem.wingsOpen != true)
        {
            //This searches for movable wings on the ship if they haven't already been loaded
            if (wingSystem.wings == null)
            {
                FindMovableWings(wingSystem);
            }

            //This indicates to other functions whether the wings are open or closed
            wingSystem.wingsOpen = true;

            //This activates the wing rotation
            if (wingSystem.wing01 != null & wingSystem.wing01_open != null & wingSystem.wing01_closed != null)
            {
                SnapToWingPosition(wingSystem.wing01, wingSystem.wing01_open.transform, wingSystem.wing01_closed.transform, 2, true);
            }

            if (wingSystem.wing02 != null & wingSystem.wing02_open != null & wingSystem.wing02_closed != null)
            {
                SnapToWingPosition(wingSystem.wing02, wingSystem.wing02_open.transform, wingSystem.wing02_closed.transform, 2, true);
            }

            if (wingSystem.wing03 != null & wingSystem.wing03_open != null & wingSystem.wing03_closed != null)
            {
                SnapToWingPosition(wingSystem.wing03, wingSystem.wing03_open.transform, wingSystem.wing03_closed.transform, 2, true);
            }

            if (wingSystem.wing04 != null & wingSystem.wing04_open != null & wingSystem.wing04_closed != null)
            {
                SnapToWingPosition(wingSystem.wing04, wingSystem.wing04_open.transform, wingSystem.wing04_closed.transform, 2, true);
            }
        }
    }

    //Snaps the wings shut
    public static void SnapClosedWings(WingSystems wingSystems)
    {
        if (wingSystems == null)
        {
            return;
        }

        wingSystems.wingsOverridden = false;

        if (wingSystems.wingsOpen != false)
        {
            //This searches for movable wings on the ship if they haven't already been loaded
            if (wingSystems.wings == null)
            {
                FindMovableWings(wingSystems);
            }

            //This indicates to other functions whether the wings are open or closed
            wingSystems.wingsOpen = false;

            //This activates the wing rotation
            if (wingSystems.wing01 != null & wingSystems.wing01_open != null & wingSystems.wing01_closed != null)
            {
                SnapToWingPosition(wingSystems.wing01, wingSystems.wing01_open.transform, wingSystems.wing01_closed.transform, 2, false);
            }

            if (wingSystems.wing02 != null & wingSystems.wing02_open != null & wingSystems.wing02_closed != null)
            {
                SnapToWingPosition(wingSystems.wing02, wingSystems.wing02_open.transform, wingSystems.wing02_closed.transform, 2, false);
            }

            if (wingSystems.wing03 != null & wingSystems.wing03_open != null & wingSystems.wing03_closed != null)
            {
                SnapToWingPosition(wingSystems.wing03, wingSystems.wing03_open.transform, wingSystems.wing03_closed.transform, 2, false);
            }

            if (wingSystems.wing04 != null & wingSystems.wing04_open != null & wingSystems.wing04_closed != null)
            {
                SnapToWingPosition(wingSystems.wing04, wingSystems.wing04_open.transform, wingSystems.wing04_closed.transform, 2, false);
            }
        }
    }

    //This snaps a wing to the desinated position
    public static void SnapToWingPosition(GameObject wing, Transform openPosition, Transform closePosition, float speed, bool open)
    {
        Quaternion startRotation = closePosition.localRotation;
        Quaternion endRotation = openPosition.localRotation;

        if (open == false)
        {
            startRotation = openPosition.localRotation;
            endRotation = closePosition.localRotation;
        }

        wing.transform.localRotation = endRotation;
    }

    #region landing gear (optional, landing sequences only)

    // True when this ship model has deployable landing gear (AGB gunboat...).
    // Ships without gear markers (X-Wing, TIE...) ignore every gear command.
    // Gear is driven ONLY by landing/docking/takeoff sequences: the manual
    // wing toggle (V) and hyperspace never touch it.
    public static bool HasMovableGear(WingSystems wingSystem)
    {
        if (wingSystem == null)
        {
            return false;
        }

        if (wingSystem.gearNodes == null)
        {
            FindMovableGear(wingSystem);
        }

        return wingSystem.gearNodes != null & wingSystem.gearNodes.Length > 0;
    }

    //Deploys the landing gear (animated, ~2.5s like the authored clips).
    //Gear markers hold full local poses (position AND rotation): legs
    //translate down, hatches rotate open.
    public static void DeployGear(WingSystems wingSystem)
    {
        if (wingSystem == null)
        {
            return;
        }

        if (wingSystem.gearDown == true)
        {
            return;
        }

        if (wingSystem.gearNodes == null)
        {
            FindMovableGear(wingSystem);
        }

        wingSystem.gearDown = true;

        if (HasMovableGear(wingSystem) == false)
        {
            return;
        }

        for (int i = 0; i < wingSystem.gearNodes.Length; i++)
        {
            if (wingSystem.gearNodes[i] != null & wingSystem.gearUpMarkers[i] != null & wingSystem.gearDownMarkers[i] != null)
            {
                Task a = new Task(LerpToGearPose(wingSystem.gearNodes[i], wingSystem.gearUpMarkers[i].transform, wingSystem.gearDownMarkers[i].transform, 2.5f, false));
            }
        }
    }

    //Retracts the landing gear (animated)
    public static void RetractGear(WingSystems wingSystem)
    {
        if (wingSystem == null)
        {
            return;
        }

        if (wingSystem.gearDown == false)
        {
            return;
        }

        if (wingSystem.gearNodes == null)
        {
            FindMovableGear(wingSystem);
        }

        wingSystem.gearDown = false;

        if (HasMovableGear(wingSystem) == false)
        {
            return;
        }

        for (int i = 0; i < wingSystem.gearNodes.Length; i++)
        {
            if (wingSystem.gearNodes[i] != null & wingSystem.gearUpMarkers[i] != null & wingSystem.gearDownMarkers[i] != null)
            {
                Task a = new Task(LerpToGearPose(wingSystem.gearNodes[i], wingSystem.gearUpMarkers[i].transform, wingSystem.gearDownMarkers[i].transform, 2.5f, true));
            }
        }
    }

    //Snaps the gear deployed (hangar display, no animation)
    public static void SnapGearDown(WingSystems wingSystems)
    {
        if (wingSystems == null)
        {
            return;
        }

        if (wingSystems.gearNodes == null)
        {
            FindMovableGear(wingSystems);
        }

        wingSystems.gearDown = true;

        if (HasMovableGear(wingSystems) == false)
        {
            return;
        }

        for (int i = 0; i < wingSystems.gearNodes.Length; i++)
        {
            if (wingSystems.gearNodes[i] != null & wingSystems.gearUpMarkers[i] != null & wingSystems.gearDownMarkers[i] != null)
            {
                SnapToGearPose(wingSystems.gearNodes[i], wingSystems.gearUpMarkers[i].transform, wingSystems.gearDownMarkers[i].transform, false);
            }
        }
    }

    //Snaps the gear retracted (no animation)
    public static void SnapGearUp(WingSystems wingSystems)
    {
        if (wingSystems == null)
        {
            return;
        }

        if (wingSystems.gearNodes == null)
        {
            FindMovableGear(wingSystems);
        }

        wingSystems.gearDown = false;

        if (HasMovableGear(wingSystems) == false)
        {
            return;
        }

        for (int i = 0; i < wingSystems.gearNodes.Length; i++)
        {
            if (wingSystems.gearNodes[i] != null & wingSystems.gearUpMarkers[i] != null & wingSystems.gearDownMarkers[i] != null)
            {
                SnapToGearPose(wingSystems.gearNodes[i], wingSystems.gearUpMarkers[i].transform, wingSystems.gearDownMarkers[i].transform, true);
            }
        }
    }

    //This lerps a gear node between its up and down marker poses
    //(position and rotation: legs translate, hatches rotate).
    public static IEnumerator LerpToGearPose(GameObject gearNode, Transform upPose, Transform downPose, float duration, bool retract)
    {
        Vector3 startPosition = downPose.localPosition;
        Quaternion startRotation = downPose.localRotation;
        Vector3 endPosition = upPose.localPosition;
        Quaternion endRotation = upPose.localRotation;

        if (retract == false)
        {
            startPosition = upPose.localPosition;
            startRotation = upPose.localRotation;
            endPosition = downPose.localPosition;
            endRotation = downPose.localRotation;
        }

        float timeElapsed = 0;

        while (timeElapsed < duration)
        {
            if (gearNode != null)
            {
                gearNode.transform.localPosition = Vector3.Lerp(startPosition, endPosition, timeElapsed / duration);
                gearNode.transform.localRotation = Quaternion.Lerp(startRotation, endRotation, timeElapsed / duration);
            }

            timeElapsed += Time.deltaTime;
            yield return null;
        }

        if (gearNode != null)
        {
            gearNode.transform.localPosition = endPosition;
            gearNode.transform.localRotation = endRotation;
        }
    }

    //This snaps a gear node to its up or down marker pose.
    public static void SnapToGearPose(GameObject gearNode, Transform upPose, Transform downPose, bool retract)
    {
        if (retract == true)
        {
            gearNode.transform.localPosition = upPose.localPosition;
            gearNode.transform.localRotation = upPose.localRotation;
        }
        else
        {
            gearNode.transform.localPosition = downPose.localPosition;
            gearNode.transform.localRotation = downPose.localRotation;
        }
    }

    //This finds any landing gear that can be deployed on the craft.
    //Convention (same pattern as wings): nodes named gear01..gearNN with
    //markers gearXX_up / gearXX_down holding the two poses. Nodes may live at
    //any depth (e.g. under the Model node), so this searches recursively.
    public static void FindMovableGear(WingSystems wingSystem)
    {
        List<GameObject> nodes = new List<GameObject>();
        List<GameObject> upMarkers = new List<GameObject>();
        List<GameObject> downMarkers = new List<GameObject>();

        for (int i = 1; i <= 12; i++)
        {
            string id = i.ToString("00");
            Transform node = FindDeepChildCalled(wingSystem.transform, "gear" + id);

            if (node == null)
            {
                continue;
            }

            Transform up = FindDeepChildCalled(wingSystem.transform, "gear" + id + "_up");
            Transform down = FindDeepChildCalled(wingSystem.transform, "gear" + id + "_down");

            if (up == null || down == null)
            {
                continue;
            }

            nodes.Add(node.gameObject);
            upMarkers.Add(up.gameObject);
            downMarkers.Add(down.gameObject);
        }

        wingSystem.gearNodes = nodes.ToArray();
        wingSystem.gearUpMarkers = upMarkers.ToArray();
        wingSystem.gearDownMarkers = downMarkers.ToArray();
    }

    //Recursive exact-name child search (FindChildTransformCalled only looks
    //at direct children, but gear/wing nodes can sit under Model or holders).
    private static Transform FindDeepChildCalled(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
            {
                return child;
            }

            Transform found = FindDeepChildCalled(child, name);

            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    #endregion

    //This finds any wings that can be open and closed on the craft    
    public static void FindMovableWings(WingSystems wingSystem)
    {
        wingSystem.wings = GameObjectUtils.FindAllChildTransformsContaining(wingSystem.transform, "wing");

        if (wingSystem.wings != null)
        {
            foreach (Transform wing in wingSystem.wings)
            {
                if (wing.name == "wing01")
                {
                    wingSystem.wing01 = wing.gameObject;
                }
                else if (wing.name == "wing02")
                {
                    wingSystem.wing02 = wing.gameObject;
                }
                else if (wing.name == "wing03")
                {
                    wingSystem.wing03 = wing.gameObject;
                }
                else if (wing.name == "wing04")
                {
                    wingSystem.wing04 = wing.gameObject;
                }
                else if (wing.name == "wing01_open")
                {
                    wingSystem.wing01_open = wing.gameObject;
                }
                else if (wing.name == "wing02_open")
                {
                    wingSystem.wing02_open = wing.gameObject;
                }
                else if (wing.name == "wing03_open")
                {
                    wingSystem.wing03_open = wing.gameObject;
                }
                else if (wing.name == "wing04_open")
                {
                    wingSystem.wing04_open = wing.gameObject;
                }
                else if (wing.name == "wing01_closed")
                {
                    wingSystem.wing01_closed = wing.gameObject;
                }
                else if (wing.name == "wing02_closed")
                {
                    wingSystem.wing02_closed = wing.gameObject;
                }
                else if (wing.name == "wing03_closed")
                {
                    wingSystem.wing03_closed = wing.gameObject;
                }
                else if (wing.name == "wing04_closed")
                {
                    wingSystem.wing04_closed = wing.gameObject;
                }
            }
        }
    }
}
