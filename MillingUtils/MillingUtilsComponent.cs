using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using Rhino.Geometry.Intersect;
using static Rhino.Render.ChangeQueue.Light;

namespace MillingUtils
{
    public class MillingUtilsComponent : GH_Component
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public MillingUtilsComponent()
          : base("MillingUtils", "MU",
            "Offset multiple Pockets for milling purposes",
            "MillingUtils", "Utils")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Part", "P", "The Part that needs to be cut", GH_ParamAccess.item);
            pManager.AddCurveParameter("Cutouts", "C", "The cutouts that need to be extended", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Plane", "P", "The Plane in which the part lay", GH_ParamAccess.item);
            pManager[2].Optional = true;
            pManager.AddNumberParameter("Offset", "O", "The value to offset the cutouts to the exterior", GH_ParamAccess.item);
            pManager[3].Optional = true;
            pManager.AddBooleanParameter("IsHole", "H", "Boolean value for the part to be a Hole or not", GH_ParamAccess.item);
            pManager[4].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("OutCutouts", "OC", "Output test Cutouts", GH_ParamAccess.list);
            pManager.AddCurveParameter("test", "t", "t", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // REQUIREMENTS:           
            // 1. Everything is drawn on XY plane -> Check if not
            // 2. Only one overlap Segment per cutout

            Curve part = null;

            if (!DA.GetData(0, ref part)) { return; }
            if (!part.IsValid) { return; }

            List<Curve> cutouts = new List<Curve>();
            if (!DA.GetDataList(1, cutouts)) { return; }
            if (cutouts.Count == 0) { return; }

            Plane plane = Plane.WorldXY;
            DA.GetData(2, ref plane);

            double offset = 10;
            DA.GetData(3, ref offset);

            bool isHole = false;
            DA.GetData(4, ref isHole);

            // Checks on Planarity and Direction
            State state = PreliminaryCheck(ref part, ref cutouts, isHole);

            if (state == State.PartNonPlanar)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The curve input is not planar");
                return;
            }
            if (state == State.CutoutsNonPlanar)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one of the cutouts input is not planar");
                return;
            }

            // Set output List
            List<Curve> outputCutouts = new List<Curve>();
            List<Curve> tests = new List<Curve>();

            foreach (Curve cutout in cutouts)
            {
                double overlapLength = GetOverlapLength(cutout, part, 0.01, 0.01);
                if (overlapLength > 0)
                {
                    Curve test;
                    Curve finalCutout = GetFinalCutout(part, cutout, plane, offset, out test);

                    if (finalCutout != null)
                    {
                        outputCutouts.Add(finalCutout);
                        tests.Add(test);
                    }
                }
            }

            DA.SetDataList(0, outputCutouts);
            DA.SetDataList(1, tests);
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => null;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("E9C9783F-3F14-484C-93FF-0ED78300EB28");

        public Curve GetFinalCutout(Curve part, Curve cutout, Plane plane, double offset, out Curve test)
        {
            Curve joinedCurve = null;
            Curve t = null;

            double tolerance = .01, overlapTolerance = .01;

            CurveIntersections intersections = Intersection.CurveCurve(part, cutout, tolerance, overlapTolerance);

            if (intersections != null)
            {
                List<Curve> curvesToJoin = new List<Curve>();

                foreach (IntersectionEvent eventX in intersections)
                    if (eventX.IsOverlap)
                        curvesToJoin.Add(cutout.Trim(eventX.OverlapB[0], eventX.OverlapB[1]));

                Curve trimmedCurveToOffset = Curve.JoinCurves(curvesToJoin)[0];

                Curve[] offsetCurves = trimmedCurveToOffset.Offset(plane, offset, tolerance, CurveOffsetCornerStyle.Sharp);
                Curve offsetCurve = offsetCurves.Length == 1 ? offsetCurves[0] : Curve.JoinCurves(offsetCurves)[0];

                //Curve[] splitCurves = cutout.Split(new double[] { trimmedCurveToOffset.Domain.T0, trimmedCurveToOffset.Domain.T1 });

                //Curve trimmedCurveToJoin = splitCurves[0].Domain.IncludesParameter(trimmedCurveToOffset.Domain.T0) ? splitCurves[1] : splitCurves[0];

                ////TODO Check for multiple results
                //joinedCurve = Curve.JoinCurves(new List<Curve>
                //        {
                //            trimmedCurveToJoin,
                //            new Line(trimmedCurveToJoin.PointAtStart, offsetCurve.PointAtEnd).ToNurbsCurve(),
                //            offsetCurve,
                //            new Line(trimmedCurveToJoin.PointAtEnd, offsetCurve.PointAtStart).ToNurbsCurve(),
                //        }
                //)[0];

                joinedCurve = offsetCurve;
                t = trimmedCurveToOffset;
            }

            test = t;
            return joinedCurve;
        }

        public static double GetOverlapLength(Curve curveA, Curve curveB, double tolerance, double overlapTolerance)
        {
            CurveIntersections intersections = Intersection.CurveCurve(curveA, curveB, tolerance, overlapTolerance);
            double totalOverlapLength = 0;

            if (intersections != null)
            {
                foreach (IntersectionEvent eventX in intersections)
                {
                    if (eventX.IsOverlap)
                    {
                        double length = curveA.GetLength(new Interval(eventX.OverlapA[0], eventX.OverlapA[1]));
                        totalOverlapLength += length;
                    }
                }
            }

            return totalOverlapLength;
        }

        public State PreliminaryCheck(ref Curve part, ref List<Curve> cutouts, bool isHole)
        {
            // Check for 
            if (!part.IsPlanar())
                return State.PartNonPlanar;
            if (cutouts.Any(x => !x.IsPlanar()))
                return State.CutoutsNonPlanar;

            Plane plane;
            part.TryGetPlane(out plane);

            if (plane.Normal.Z > 0 & isHole)
                part.Reverse();

            foreach (Curve cutout in cutouts)
            {
                Plane plane1;
                part.TryGetPlane(out plane1);

                if (plane1.Normal.Z > 0 & isHole)
                    cutout.Reverse();
            }

            return State.Valid;
        }

        public enum State
        {
            PartNonPlanar,
            CutoutsNonPlanar,
            Valid
        }
    }
}