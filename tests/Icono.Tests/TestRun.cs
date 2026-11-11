using Xunit;

// Some code reads the drawing scale from a global (DrawingScale.Denominator), which many tests set: the tests run one after the other, so
// none of them can see the scale another one has set at the same moment.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
