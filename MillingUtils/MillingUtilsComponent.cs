using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using Rhino.Geometry.Intersect;

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
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //pManager.AddCurveParameter("Out", "O", "Output Cutouts", GH_ParamAccess.list);
            pManager.AddCurveParameter("OutSegments", "OS", "Output test Cutouts", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //TODO check orientation of all curves

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

            // Set output List
            List<Curve> outputSegments = new List<Curve>();

            foreach (Curve cutout in cutouts)
            {
                double overlapLength = GetOverlapLength(cutout, part, 0.01, 0.01);
                if (overlapLength > 0)
                {
                    var segment = GetOverlapSegment(part, cutout, 0.01, 0.01);
                    //var offsetCurves = cutout.Offset(plane, offset, 1e-5, CurveOffsetCornerStyle.Round);

                    //if (offsetCurves != null)
                    //    foreach (Curve oc in offsetCurves)
                    //        outputs.Add(oc);
                    //else
                    //    outputs.Add(cutout);

                    if (segment != null)
                    {
                        outputSegments.AddRange(segment);
                    }
                }
            }

            DA.SetDataList(0, outputSegments);
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

        public List<Curve> GetOverlapSegment(Curve part, Curve cutout, double tolerance, double overlapTolerance)
        {
            //TODO Get all point of a segment, or get the segment itself.
            CurveIntersections intersections = Intersection.CurveCurve(part, cutout, tolerance, overlapTolerance);
            List<Curve> overlappedCurves = new List<Curve>();

            if (intersections != null)
            {
                foreach (IntersectionEvent eventX in intersections)
                {
                    if (eventX.IsOverlap)
                    {
                        Curve partToTrim = part.DuplicateCurve();

                        partToTrim.Trim(eventX.OverlapA[0], eventX.OverlapA[1]);

                        overlappedCurves.Add(partToTrim);
                    }
                }
            }

            return overlappedCurves;
        }
    }
}