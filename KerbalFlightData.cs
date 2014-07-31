﻿/*
    Copyright 2014 DaMichel
 
    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Text;
using UnityEngine;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;


public class DMDebug
{
#if DEBUG
    StringBuilder sb = new StringBuilder();
    //Dictionary visited = new Dictionary<UnityEngine.Object, String>();
    HashSet<int> visited = new HashSet<int>(); //UnityEngine.Object

    bool CheckAndAddVisited(UnityEngine.Object o)
    {
        int key = o.GetInstanceID();
        if (visited.Contains(key)) return true;
        visited.Add(key);
        return false;
    }

    void Out(String s, int indent)
    {
        var indentStr = new String(' ', indent);
        var arr = s.Split('\n');
        foreach (String s_ in arr)
        {
            String tmp = s_.Trim('\n', '\r', ' ', '\t').Trim();
            if (tmp.Length == 0) continue;
            sb.AppendLine(indentStr + tmp);
        }
    }

    bool IsInterestingType(Type typeToCheck)
    {
        var types = new Type[] {
            typeof(UnityEngine.Component),
            typeof(UnityEngine.GameObject),
            typeof(UnityEngine.Renderer),
            typeof(UnityEngine.Mesh),
            typeof(UnityEngine.Material),
            typeof(UnityEngine.Texture)
        };
        foreach (Type t in types)
        {
            if(t.IsAssignableFrom(typeToCheck)) return true;
        }
        return false;
    }

    bool IsOkayToExpand(string name, Type type)
    {
        if (!IsInterestingType(type)) return false;
        if (name == "parent" || name == "root" || name == "target") return false;
        return true;
    }

    public void PrintHierarchy(UnityEngine.Object instance, int indent = 0)
    {
        try 
        {
            if (instance==null || CheckAndAddVisited(instance)) return;

            var t = instance.GetType();
            Out("+ " + instance.name + "(" + t.Name + ")", indent); //<" + instance.GetInstanceID() + ">"

            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {   
                var value = field.GetValue(instance);
                Out(field.FieldType.Name + " " + field.Name + " = " + value, indent + 1);
                if (IsOkayToExpand(field.Name, field.FieldType))
                {
                    PrintHierarchy((UnityEngine.Object)value, indent + 2);
                }
            }
            foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                object value = null; 
                try {
                 value = prop.GetValue(instance, null);
                } catch(Exception e) {
                 value = e.ToString();
                }
                Out(prop.PropertyType.Name + " " + prop.Name + " = " + value, indent + 1);
                if (IsOkayToExpand(prop.Name, prop.PropertyType))
                {
                    PrintHierarchy((UnityEngine.Object)value, indent + 2);
                }
            }

            if (typeof(GameObject).IsAssignableFrom(t))
            {
                Out("[Components of "+instance.name+" ]", indent+1);
                foreach (var comp in ((GameObject)instance).GetComponents<Component>())
                {
                    PrintHierarchy(comp, indent+2);
                }
            }
        }
        catch (Exception e)
        {
            Out("Error: " + e.ToString(), indent);
        }
    }

    public String ToString()
    {
        return sb.ToString();
    }
#endif

    public static void Log(string s)
    {
        Debug.Log("DMFlightData: "+s);
    }

    public static void LogWarning(string s)
    {
        Debug.LogWarning("DMFlightData: " + s);
    }
}


public class GuiInfo
{
    NavBallBurnVector burnVector = null;

    float navBallLeftBoundary;
    float navBallRightBoundary;
    float navBallRightBoundaryWithGauge;

    public GuiInfo()
    {
        Update();
    }

    public void Update()
    {        
        DMDebug.Log("Update ");
        ScreenSafeUI ui = ScreenSafeUI.fetch;
        GameObject navballGameObject = GameObject.Find("NavBall");
        GameObject maneuverVectorGameObject = GameObject.Find("maneuverVector");
        burnVector = maneuverVectorGameObject.GetComponent<NavBallBurnVector>();
        Camera cam = ScreenSafeUI.referenceCam;

        float navballWidth = 0.07f;
        float navballWidthWithGauge = 0.12f;

        Vector3 p = cam.WorldToScreenPoint(navballGameObject.transform.position);
        Vector3 p2 = cam.WorldToScreenPoint(navballGameObject.transform.localToWorldMatrix.MultiplyPoint(new Vector3(navballWidth, 0, 0)));
        Vector3 p3 = cam.WorldToScreenPoint(navballGameObject.transform.localToWorldMatrix.MultiplyPoint(new Vector3(navballWidthWithGauge, 0, 0)));
        navBallRightBoundary = p2.x;
        navBallLeftBoundary = p.x - (p2.x - p.x);
        navBallRightBoundaryWithGauge = p3.x;

        ui.centerAnchor.bottom.hasChanged = false; // this is probably not a good idea. It will break things that also use the hasChanged flag ...
    }

    public bool hasChanged
    {
        get { return ScreenSafeUI.fetch.centerAnchor.bottom.hasChanged; }
    }

    public bool showGauge
    {
        get { return burnVector.deltaVGauge != null && burnVector.deltaVGauge.gameObject.activeSelf == true; }
    }

    public float screenAnchorLeft
    {
        get { return navBallLeftBoundary; }
    }

    public float screenAnchorRight
    {
        get { return showGauge ? navBallRightBoundaryWithGauge : navBallRightBoundary; }
    }
}


[KSPAddon(KSPAddon.Startup.Flight, false)]
public class DMFlightData : MonoBehaviour
{
    double machNumber;
    double airAvailability;
    double stallPercentage;
    double q;
    int    warnQ = 0;
    int    warnStall = 0;
    int    warnAir = 0;
    int    warnTemp = 0;
    double apoapsis = 0;
    double periapsis = 0;
    double altitude = 0;
    double radarAltitude = 0;
    double timeToNode = 0;
    enum NextNode {
        Ap, Pe, Escape, Maneuver, Encounter
    };
    NextNode nextNode = NextNode.Ap;
    double highestTemp = 0;
    double highestRelativeTemp = 0;
    double dtSinceLastUpdate = 0;

    float  uiScalingFactor = 1f;

    bool   displayOrbitalData = false;
    bool   displayAtmosphericData = false;
    bool   farDataIsObtainedOkay = true;
    bool   displayUI = true;
    bool   displayUIByGuiEvent = true;

    bool hasDRE   = false;
    Type FARControlSys = null;
    GuiInfo guiInfo = null;

    const double updateIntervall = 0.1;
    int timeSecondsPerDay = 0;
    int timeSecondsPerYear = 0;

    static Toolbar.IButton toolbarButton;

    #region Config & Data Acquisition
    void UpdateFARData()
    {
        var instance = FARControlSys.GetField("activeControlSys", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        foreach (var field in FARControlSys.GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public))
        {
            if (field.Name == "q")
                q = (double)field.GetValue(instance);
            else if (field.Name == "MachNumber")
                machNumber = (double)field.GetValue(instance);
            else if (field.Name == "intakeDeficit")
                airAvailability = (double)field.GetValue(null);
            else if (field.Name == "stallPercentage")
                stallPercentage = (double)field.GetValue(null);
        }
    }


    void Awake()
    {
        //DMDebug.Log("DMFlightData Awake");
        foreach (var assembly in AssemblyLoader.loadedAssemblies)
        {
            //DMDebug.Log(assembly.name);
            if (assembly.name == "FerramAerospaceResearch")
            {
                var types = assembly.assembly.GetExportedTypes(); 
                foreach (Type t in types)
                {
                    //DMDebug.Log(t.FullName);
                    if (t.FullName.Equals("ferram4.FARControlSys")) 
                    {
                        FARControlSys = t;
                    }
                }
            }
            if (assembly.name == "DeadlyReentry")
            {
                hasDRE = true;
            }
        }

        if (GameSettings.KERBIN_TIME)
        {
            CelestialBody b = FlightGlobals.Bodies.Find((b_) => b_.name == "Kerbin");
            timeSecondsPerDay  = (int)b.rotationPeriod;
            timeSecondsPerYear = (int)b.orbit.period;
            // when this fails an exception should be visible in the debug log and 
            // the time to the next node display should show NaNs and Infs so it should
            // be pretty clear when something goes wrong at this place
        }
        else
        {
            timeSecondsPerDay = 24 * 3600;
            timeSecondsPerYear = timeSecondsPerDay * 365;
        }

        guiInfo = new GuiInfo();

        toolbarButton = Toolbar.ToolbarManager.Instance.add("KerbalFlightData", "damichelsflightdata");
        toolbarButton.TexturePath = "KerbalFlightData/toolbarbutton";
        toolbarButton.ToolTip = "KerbalFlightData On/Off Switch";
        toolbarButton.Visibility = new Toolbar.GameScenesVisibility(GameScenes.FLIGHT);
        toolbarButton.Enabled = true;
        toolbarButton.OnClick += (e) =>
        {
            enabled = !enabled;
        };

        GameEvents.onHideUI.Add(() => 
        {
            displayUIByGuiEvent = false;
        });

        GameEvents.onShowUI.Add(() =>
        {
            displayUIByGuiEvent = true;
        });
    }


    public void SaveSettings()
    {
        ConfigNode settings = new ConfigNode();
        settings.name = "SETTINGS";
        settings.AddValue("active", enabled);
        settings.Save(AssemblyLoader.loadedAssemblies.GetPathByType(typeof(DMFlightData)) + "/settings.cfg");
    }


    public void LoadSettings()
    {
        ConfigNode settings = new ConfigNode();
        settings = ConfigNode.Load(AssemblyLoader.loadedAssemblies.GetPathByType(typeof(DMFlightData)) + @"\settings.cfg".Replace('/', '\\'));

        if (settings != null)
        {
            if (settings.HasValue("active")) enabled = bool.Parse(settings.GetValue("active"));
        }
    }


	void Start()
    {
        LoadSettings();

        dtSinceLastUpdate = Double.PositiveInfinity;
    }


    public void OnDestroy()
    {
        SaveSettings();
    }


    void LateUpdate()
    {
#if !DEBUG
        if (dtSinceLastUpdate > updateIntervall) // update every so and so fraction of a second
        {
            dtSinceLastUpdate = 0;
        }
        else
        {
            dtSinceLastUpdate += Time.deltaTime;
            return;
        }
#endif 
        if (guiInfo.hasChanged)
            guiInfo.Update();

        displayUI = false; // don't show anything unless some stuff is all right
        if (!FlightGlobals.ready) return;

        Vessel vessel = FlightGlobals.ActiveVessel;
        if (vessel == null) return;

        if (vessel.isEVA || vessel.state == Vessel.State.DEAD)
        {
            return;
        }
        else displayUI = true; // at this point something should probably be shown

        try
        {
            UpdateFARData();
            if (!farDataIsObtainedOkay)
                DMDebug.Log("Data from FAR obtained successfully");
            farDataIsObtainedOkay = true;
        }
        catch (Exception e)
        {
            if (farDataIsObtainedOkay) // if it was obtained okay the last time
            {
                DMDebug.Log(e.ToString());
                farDataIsObtainedOkay = false;
            }
            // otherwise remain silent!
        }

        if (q < 10)
        {
            warnQ = 0;
            warnStall = 0;
        }
        else
        {
            if (q > 40000)
                warnQ = 1;
            else 
                warnQ = 0;

            if (stallPercentage > 0.5)
                warnStall = 2;
            else if (stallPercentage > 0.005)
                warnStall = 1;
            else
                warnStall = 0;
        }
        if (airAvailability < 1.05)
            warnAir = 2;
        else if (airAvailability < 1.5)
            warnAir = 1;
        else
            warnAir = 0;
        
        Orbit o = vessel.orbit;
        CelestialBody b = vessel.mainBody;

        displayAtmosphericData = farDataIsObtainedOkay;
        displayOrbitalData = false;
        if (o != null && b != null)
        {
            periapsis = o.PeA;
            apoapsis = o.ApA;

            double time = Planetarium.GetUniversalTime();
            double timeToEnd = o.EndUT - time;
            double timeToAp  = o.timeToAp;
            double timeToPe  = o.timeToPe;

            if (apoapsis < periapsis || timeToAp <= 0)
                timeToAp = double.PositiveInfinity; // not gona happen
            if (timeToPe <= 0)
                timeToPe = double.PositiveInfinity;               

            if (timeToEnd <= timeToPe && timeToEnd <= timeToAp && o.patchEndTransition != Orbit.PatchTransitionType.FINAL && o.patchEndTransition != Orbit.PatchTransitionType.INITIAL)
            {
                timeToNode = timeToEnd;
                if (o.patchEndTransition == Orbit.PatchTransitionType.ESCAPE)    nextNode = NextNode.Escape;
                else if (o.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER) nextNode = NextNode.Encounter;
                else nextNode = NextNode.Maneuver;
            }
            else if (timeToAp < timeToPe)
            {
                timeToNode = o.timeToAp;
                nextNode   = NextNode.Ap;
            }
            else
            {
                timeToNode = timeToPe;
                nextNode   = NextNode.Pe;
            }

            if (b.atmosphere)
            {
                double hmin = (double)(((int)(b.maxAtmosphereAltitude * 0.33333333e-3)))*1000;
                displayOrbitalData = apoapsis>hmin || periapsis>hmin;
            }
            else
                displayOrbitalData = true;
   
            displayAtmosphericData &= b.atmosphere && vessel.altitude<b.maxAtmosphereAltitude;

#if DEBUG
            if (Input.GetKeyDown(KeyCode.I))
            {
                guiInfo = new GuiInfo();
            }
            if (Input.GetKeyDown(KeyCode.O))
            {
                DMDebug dbg = new DMDebug();
                //dbg.PrintHierarchy(ScreenSafeUI.fetch);
                //dbg.PrintHierarchy(GameObject.Find("collapseExpandButton"));
                //dbg.PrintHierarchy(ScreenSafeUI.fetch.centerAnchor.bottom);
                //dbg.PrintHierarchy(GameObject.Find("NavBall"));
                dbg.PrintHierarchy(GameObject.Find("maneuverVector"));
                var f = KSP.IO.TextWriter.CreateForType<DMFlightData>("DMdebugoutput.txt", null);
                f.Write(dbg.ToString());
                f.Close();
            }
#endif
        }
        altitude = vessel.altitude;
        radarAltitude = vessel.altitude - Math.Max(0, vessel.terrainAltitude); // terrainAltitude is the deviation of the terrain from the sea level.

        if (hasDRE)
        {
            highestTemp = double.NegativeInfinity;
            highestRelativeTemp = double.NegativeInfinity;
            foreach (Part p in vessel.parts)
            {
                if (p.temperature != 0f) // small gear box has p.temperature==0 - always! Bug? Who knows. Anyway i want to ignore it.
                {
                    highestTemp = Math.Max(p.temperature, highestTemp);
                    highestRelativeTemp = Math.Max(p.temperature/p.maxTemp, highestRelativeTemp);
                }
            }
            if (highestRelativeTemp > 0.95)
                warnTemp = 2;
            else if (highestRelativeTemp > 0.8)
                warnTemp = 1;
            else
                warnTemp = 0;
        }
    }
    #endregion

    #region GUIUtil
    protected static String FormatPressure(double x)
    {
        if (x > 1.0e6)
        {
            x *= 1.0e-6;
            return x.ToString(x < 10 ? "F1" : "F0") + " MPa";
        }
        else
        {
            x *= 1.0e-3;
            return x.ToString(x < 10 ? "F1" : "F0") + " kPa";
        }
    }

    protected static String FormatAltitude(double x)
    {
        double a = Math.Abs(x);
        if (a >= 1.0e9)
        {
            x *= 1.0e-9;
            a *= 1.0e-9;
            return x.ToString(a < 10 ? "F2" : (a < 100 ? "F1" : "F0")) + " Gm";
        }
        if (a >= 1.0e6)
        {
            x *= 1.0e-6;
            a *= 1.0e-6;
            return x.ToString(a < 10 ? "F2" : (a < 100 ? "F1" : "F0")) + " Mm";
        }
        else
        {
            x *= 1.0e-3;
            a *= 1.0e-3;
            return x.ToString(a < 10 ? "F2" : (a < 100 ? "F1" : "F0")) + " km";
        }
    }


    protected static String FormatRadarAltitude(double x)
    {
        double a = Math.Abs(x);
        if (a >= 1.0e3)
        {
            x *= 1.0e-3;
            a *= 1.0e-3;
            return x.ToString(a < 10 ? "F2" : (a < 100 ? "F1" : "F0")) + " km";
        }
        else
        {
            return x.ToString("F0") + " m";
        }
    }


    protected String FormatTime(double x_)
    {
        const int MIN = 60;
        const int H   = 3600;
        int D         = timeSecondsPerDay;
        int Y         = timeSecondsPerYear;

        int x = (int)x_;
        int y, d, m, h, s;
        y = x/Y;
        x = x%Y;
        d = x/D;
        x = x%D;
        h = x/H;
        x = x%H;
        m = x/MIN;
        x = x%MIN;
        s = x;
        int size = 3;
        string [] arr = new string[size];
        int idx = 0;
        if (y > 0)
            arr[idx++] = y.ToString()+"y";
        if (d > 0 || idx>0)
            arr[idx++] = d.ToString()+"d";
        if ((h > 0 || idx > 0) && idx < size)
            arr[idx++] = h.ToString()+"h";
        if ((m > 0  || idx>0) && idx<size)
            arr[idx++] = m.ToString()+"m";
        if ((s > 0  || idx>0) && idx<size)
            arr[idx++] = s.ToString()+"s"; 
        return string.Join(" ", arr, 0, idx);
    }
    #endregion

    #region GUI
    GUIStyle[] styles = { null, null, null };
    GUIStyle style_label = null;
    GUIStyle style_emphasized = null;
    bool guiReady = false;
    float estimatedWindowHeight1 = 100f;
    float estimatedWindowHeight2 = 100f;
    const float fontHeight = 16f; // hardcoding this is probably a bad idea. Problem is how to obtain that by code?
    const float windowBottomOffset = 5f;

    void SetupGUI()
    {
        // how much the fonts and everything must be scaled relative to a
        // reference GUI size (the normal KSP setting, i believe).
        uiScalingFactor = 0.7f / ScreenSafeUI.referenceCam.orthographicSize;
        if (Mathf.Abs(uiScalingFactor - 1f) < 0.1f) // less than 10% scaling -> no scaling to make the font look good.
            uiScalingFactor = 1f; 

        // GUI functions must only be called in OnGUI ...
        var s = new GUIStyle();
        s.hover.textColor = s.active.textColor = s.normal.textColor = s.focused.textColor = s.onNormal.textColor = s.onFocused.textColor = s.onHover.textColor = s.onActive.textColor = Color.white;
        s.padding = new RectOffset(1, 1, 1, 1);
        s.richText = true;
        
        style_label = s;
        style_emphasized = new GUIStyle(s);
        style_emphasized.fontStyle = FontStyle.Bold;

        s = new GUIStyle(s);
        s.hover.textColor = s.active.textColor = s.normal.textColor = s.focused.textColor = s.onNormal.textColor = s.onFocused.textColor = s.onHover.textColor = s.onActive.textColor = Color.grey;
        styles[0] = s;
        s = new GUIStyle(s);
        s.hover.textColor = s.active.textColor = s.normal.textColor = s.focused.textColor = s.onNormal.textColor = s.onFocused.textColor = s.onHover.textColor = s.onActive.textColor = Color.yellow;
        styles[1] = s;
        s = new GUIStyle(s);
        s.hover.textColor = s.active.textColor = s.normal.textColor = s.focused.textColor = s.onNormal.textColor = s.onFocused.textColor = s.onHover.textColor = s.onActive.textColor = Color.red;
        styles[2] = s;
        
        styles[1].fontStyle = styles[2].fontStyle = FontStyle.Bold;
        guiReady = true;
    }


    protected void OnGUI()
	{
        if (!displayUI || !displayUIByGuiEvent) return;
        {

            if (!FlightUIModeController.Instance.navBall.expanded || !FlightUIModeController.Instance.navBall.enabled) return;

            switch (CameraManager.Instance.currentCameraMode)
            {
                case CameraManager.CameraMode.IVA:
                    return;
            }
        }

        if (!guiReady) SetupGUI();

        // this is pretty messy but it has to work with different gui scaling factors.
        GUIStyle style = new GUIStyle();
        float width = 100f;
        float height = estimatedWindowHeight1;
        float pos_x = guiInfo.screenAnchorRight;
        float pos_y = Screen.height - (height + windowBottomOffset) * uiScalingFactor;

        GUI.matrix = Matrix4x4.TRS(new Vector3(pos_x, pos_y), Quaternion.identity, new Vector3(uiScalingFactor, uiScalingFactor, 1f));

        GUI.Window(
            GUIUtility.GetControlID(FocusType.Passive),
            new Rect(0, 0, width, height),
            this.DrawWindow1,
            GUIContent.none,
            style
        );

        width = 90f;
        height = estimatedWindowHeight2;
        pos_x = guiInfo.screenAnchorLeft - width;
        pos_y = Screen.height - (height + windowBottomOffset) * uiScalingFactor;

        GUI.matrix = Matrix4x4.TRS(new Vector3(pos_x, pos_y), Quaternion.identity, new Vector3(uiScalingFactor, uiScalingFactor, 1f));

        GUI.Window(
            GUIUtility.GetControlID(FocusType.Passive),
            new Rect(0, 0, width, height),
            this.DrawWindow2,
            GUIContent.none,
            style
        );

        GUI.matrix = Matrix4x4.identity;
    }

    protected void DrawWindow1(int windowId)
    {
        estimatedWindowHeight1 = 0f;
        GUILayoutOption opt = GUILayout.ExpandWidth(true);
        GUILayout.BeginVertical();
        if (displayAtmosphericData)
        {
            GUILayout.Label("Mach " + machNumber.ToString("F2"), style_emphasized, opt);
            estimatedWindowHeight1 += 1;
        }        
        if (radarAltitude < 5000)
        {
            GUILayout.Label("Alt " + FormatRadarAltitude(radarAltitude) + " R", style_label, opt);
            estimatedWindowHeight1 += 1;
        }
        else
        {
            GUILayout.Label("Alt " + FormatAltitude(altitude), style_label, opt);
            estimatedWindowHeight1 += 1;
        }
        if (displayAtmosphericData)
        {
            String intakeLabel = "Intake";
            if (airAvailability < 2d) intakeLabel += "  "+(airAvailability*100d).ToString("F0") + "%";
            GUILayout.Label(intakeLabel, styles[warnAir], opt);
            GUILayout.Label("Q  " + FormatPressure(q), styles[warnQ], opt);
            GUILayout.Label("Stall", styles[warnStall], opt);
            estimatedWindowHeight1 += 4;
        }
        GUILayout.EndVertical();
        estimatedWindowHeight1 *= fontHeight;
    }

    protected void DrawWindow2(int windowId)
    {
        estimatedWindowHeight2 = 0;
        GUILayoutOption opt = GUILayout.ExpandWidth(true);
        GUILayout.BeginVertical();
        if (displayOrbitalData)
        {   
            String timeLabel = "";
            switch (nextNode)
            {
                case NextNode.Ap: timeLabel = "T<size=8>Ap</size> -"; break;
                case NextNode.Pe: timeLabel = "T<size=8>Pe</size> -"; break;
                case NextNode.Encounter: timeLabel = "T<size=8>Enc</size> -"; break;
                case NextNode.Maneuver: timeLabel = "T<size=8>Man</size> -"; break;
                case NextNode.Escape: timeLabel = "T<size=8>Esc</size> -"; break;
            }
            GUILayout.Label(timeLabel + FormatTime(timeToNode), style_label, opt);
            estimatedWindowHeight2 += 1;
            if (nextNode == NextNode.Ap || nextNode == NextNode.Pe)
            {
                GUILayout.Label("Ap " + FormatAltitude(apoapsis), nextNode == NextNode.Ap ? style_emphasized : style_label, opt);
                GUILayout.Label("Pe " + FormatAltitude(periapsis), nextNode == NextNode.Pe ? style_emphasized : style_label, opt);
                estimatedWindowHeight2 += 2;
            }
        }
        if (hasDRE)
        {
            GUILayout.Label("T " + highestTemp.ToString("F0") + " °C", styles[warnTemp], opt);
            estimatedWindowHeight2 += 1;
        }
        GUILayout.EndVertical();
        estimatedWindowHeight2 *= fontHeight;
    }
    #endregion
}
