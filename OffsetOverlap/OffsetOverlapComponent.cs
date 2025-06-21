using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using Rhino.Geometry.Intersect;

namespace OffsetOverlap
{
    public class OffsetOverlapComponent : GH_Component
    {
        // 0 - SingleSegment; 1 - More than one segment
        int isLine;

        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public OffsetOverlapComponent()
          : base("OffsetOverlap", "OO",
            "Offset Curves that are overlapping",
            "OffsetOverlap", "Utils")
        {
            isLine = 0;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Part", "P", "The Part that needs to be cut", GH_ParamAccess.item);
            pManager.AddCurveParameter("Overlap", "Ov", "The overlap that need to be extended", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Plane", "P", "The Plane in which the Part lay: this will also be the plane for the offset", GH_ParamAccess.item);
            pManager[2].Optional = true;
            pManager.AddNumberParameter("Offset", "Of", "The value to offset the cutouts to the exterior", GH_ParamAccess.item);
            pManager[3].Optional = true;
            pManager.AddBooleanParameter("Reverse", "R", "Boolean value to reverse the offset direction:\n" +
                "true - the overlap is offset inside the Part.\n" +
                "false - the overlap is offset outside the Part.", GH_ParamAccess.item);
            pManager[4].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Offsets", "O", "Output the offset curves", GH_ParamAccess.list);
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

            bool reverse = false;
            DA.GetData(4, ref reverse);

            // Checks on Planarity and Direction
            State state = PreliminaryCheck(ref part, ref cutouts, reverse);

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
            List<Curve> outputOffsets = new List<Curve>();

            foreach (Curve cutout in cutouts)
            {
                double overlapLength = GetOverlapLength(cutout, part, 0.01, 0.01);
                if (overlapLength > 0)
                {
                    List<Curve> finalOffset = GetFinalCutout(part, cutout, plane, offset);

                    if (finalOffset != null)
                    {
                        outputOffsets.AddRange(finalOffset);
                    }
                }
            }

            DA.SetDataList(0, outputOffsets);
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => OffsetOverlap.Properties.Resources.icon;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("E9C9783F-3F14-484C-93FF-0ED78300EB28");

        public List<Curve> GetFinalCutout(Curve part, Curve overlapCandidate, Plane plane, double offset)
        {
            // Check for singleSegment overlap
            if (overlapCandidate.IsLinear())
                this.isLine = 0;
            else this.isLine = 1;

            Curve joinedCurve = null;

            // Intersections
            CurveIntersections intersections = Intersection.CurveCurve(part, overlapCandidate, .01, .01);

            if (intersections != null)
            {
                List<Curve> overlappingSegments = new List<Curve>();
                List<double> parameters = new List<double>();

                foreach (IntersectionEvent eventX in intersections)
                    if (eventX.IsOverlap)
                    {
                        Curve trimmed = overlapCandidate.Trim(
                            Math.Min(eventX.OverlapB[0], eventX.OverlapB[1]),
                            Math.Max(eventX.OverlapB[0], eventX.OverlapB[1])
                            );

                        //Check for complete overlap                        
                        if (trimmed != null)
                            overlappingSegments.Add(trimmed);
                        else
                            overlappingSegments.Add(overlapCandidate);

                        parameters.Add(eventX.OverlapB[0]);
                        parameters.Add(eventX.OverlapB[1]);
                    }

                List<Curve> overlappingCurves = Curve.JoinCurves(overlappingSegments).ToList();

                // Offsets
                List<Curve> offsetCurves = GetOffsetCurves(plane, offset, overlappingCurves);

                // Get All complementar Segments
                List<Curve> complementarCurves = GetComplementarSegment(parameters, overlapCandidate, overlappingCurves);

                Debug.WriteLine(
                    "Debug Report:\n" +
                    "isSegment = " + this.isLine + "\n" +
                    "OverlapSegmentsCount: " + overlappingCurves.Count + "\n" +
                    "ComplementarCurves: " + complementarCurves.Count
                    );

                //TODO Crash con offset 0
                if (this.isLine == 0) joinedCurve = offsetCurves[0];
                else
                {
                    if (complementarCurves.Count == 1)
                        joinedCurve = Curve.JoinCurves(new List<Curve>
                            {
                                complementarCurves[0],
                                new Line(complementarCurves[0].PointAtStart, offsetCurves[0].PointAtEnd).ToNurbsCurve(),
                                offsetCurves[0],
                                new Line(complementarCurves[0].PointAtEnd, offsetCurves[0].PointAtStart).ToNurbsCurve(),
                            }
                        )[0];
                    else
                    {
                        List<Curve> tmpList = new List<Curve>(complementarCurves.Concat(offsetCurves));
                        List<Point3d> complementarPoints = complementarCurves.Select(c => c.PointAtStart).ToList().Concat(complementarCurves.Select(c => c.PointAtEnd).ToList()).ToList();

                        foreach (Curve offsetCurve in offsetCurves)
                        {
                            tmpList.Add(new Line(offsetCurve.PointAtStart, complementarPoints.OrderBy(p => p.DistanceTo(offsetCurve.PointAtStart)).First()).ToNurbsCurve());
                            tmpList.Add(new Line(offsetCurve.PointAtEnd, complementarPoints.OrderBy(p => p.DistanceTo(offsetCurve.PointAtEnd)).First()).ToNurbsCurve());
                        }
                        joinedCurve = Curve.JoinCurves(tmpList)[0];
                    }
                }
            }

            return new List<Curve> { joinedCurve };
        }

        private List<Curve> GetOffsetCurves(Plane plane, double offset, List<Curve> overlappingCurves)
        {
            List<Curve> offsetCurves = new List<Curve>();
            foreach (Curve trimmedCurveToOffset in overlappingCurves)
            {
                Curve[] tmpOffsetCurves = trimmedCurveToOffset.Offset(plane, offset, .01, CurveOffsetCornerStyle.Sharp);
                if (tmpOffsetCurves == null)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not offset the curve");
                    return null;
                }
                else
                    offsetCurves.AddRange(tmpOffsetCurves);
            }

            offsetCurves = Curve.JoinCurves(offsetCurves).ToList();

            return offsetCurves;
        }

        private List<Curve> GetComplementarSegment(List<double> parameters, Curve overlapCandidate, List<Curve> segmentsToOffset)
        {
            parameters = parameters.Distinct().ToList();

            List<double> startParameters = new List<double>();
            List<double> endParameters = new List<double>();
            foreach (Curve segmentToOffset in segmentsToOffset)
            {
                startParameters.Add(parameters.OrderBy(p => Math.Abs(overlapCandidate.PointAt(p).DistanceTo(segmentToOffset.PointAtStart))).First());
                endParameters.Add(parameters.OrderBy(p => Math.Abs(overlapCandidate.PointAt(p).DistanceTo(segmentToOffset.PointAtEnd))).First());
            }

            List<Curve> splitCurves = overlapCandidate.Split(startParameters.Concat(endParameters).ToList()).ToList();

            splitCurves.RemoveAll(curve => segmentsToOffset.Any(segmentToOffset =>
                    curve.PointAtLength(curve.GetLength() * 0.5)
                            .DistanceTo(segmentToOffset.PointAtLength(segmentToOffset.GetLength() * 0.5)) < 10e-4));

            return splitCurves;
        }

        private static double GetOverlapLength(Curve curveA, Curve curveB, double tolerance, double overlapTolerance)
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

        private State PreliminaryCheck(ref Curve part, ref List<Curve> cutouts, bool reverse)
        {
            // Check for 
            if (!part.IsPlanar())
                return State.PartNonPlanar;
            if (cutouts.Any(x => !x.IsPlanar()))
                return State.CutoutsNonPlanar;

            // TODO Check perchè reverse non sta effettivamente controllando.
            Plane plane;
            part.TryGetPlane(out plane);

            if (plane.Normal.Z > 0 & reverse)
                part.Reverse();

            foreach (Curve cutout in cutouts)
            {
                Plane plane1;
                part.TryGetPlane(out plane1);

                if (plane1.Normal.Z > 0 & reverse)
                    cutout.Reverse();
            }

            return State.Valid;
        }

        private enum State
        {
            PartNonPlanar,
            CutoutsNonPlanar,
            Valid
        }
    }
}