// IMP-DOG-052: property contract copied from KH_WashShell; passes reused, mobile shader untouched.
Shader "KominkaHillside/Wash Shell PC Thin"
{
    Properties
    {
        _Mask ("Wash mask (R dirt, G foam, B wet)", 2D) = "black" {}
        _PosMap ("Position map (RGB mesh pos, A bone+1)", 2D) = "black" {}
        _BaseOffset ("Offset to fur surface (m, world)", Float) = 0.005
        _FoamThickness ("Foam height of the top layer (m, world)", Float) = 0.05
        _FoamColor ("Foam colour", Color) = (0.95,0.96,0.98,1)
        _FoamShadow ("Foam valley tint (multiplier)", Color) = (0.66,0.73,0.86,1)
        _FurBrightness ("Fur brightness (0.5 brown fur, 1 white fur)", Range(0,1)) = 0.5
        _DirtColor ("Dirt colour (A = max opacity)", Color) = (0.15,0.10,0.065,0.88)
        _WetDarken ("Wet darkening at wet 1", Range(0,1)) = 0.25
        _WetGloss ("Wet gloss", Range(0,2)) = 0.6
        _WetTint ("Wet cool tint (sheen colour)", Color) = (0.45,0.6,0.85,1)
        _DirtPattern ("Dirt pattern (R, multiplies the procedural mud)", 2D) = "white" {}
        _FurFlowAxis ("Fur flow axis (bind-pose mesh space, dirt streaks)", Vector) = (0,0,1,0)
        _FoamScale ("Foam frequency (1 / mesh unit; mounds x0.65, grains x2.6)", Float) = 60
        _FoamSoft ("Foam cut edge softness", Range(0.01,0.5)) = 0.12
        _FoamBump ("(unused since rev 3)", Range(0,3)) = 0.6
        _FoamRim ("Foam rim brightening", Range(0,1)) = 0.3
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        UsePass "KominkaHillside/Wash Shell/BASE"
        UsePass "KominkaHillside/Wash Shell/FOAM0"
        UsePass "KominkaHillside/Wash Shell/FOAM1"
    }
    Fallback Off
}
