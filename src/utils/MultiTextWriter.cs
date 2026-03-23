namespace ReactCodegen;

public class MultiTextWriter : TextWriter
{
    private readonly TextWriter[] _writers;

    public MultiTextWriter(params TextWriter[] writers)
    {
        _writers = writers;
    }

    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

    public override void Write(char value)
    {
        foreach (var writer in _writers)
            writer.Write(value);
    }

    public override void Write(string? value)
    {
        foreach (var writer in _writers)
            writer.Write(value);
    }

    public override void WriteLine(string? value)
    {
        foreach (var writer in _writers)
            writer.WriteLine(value);
    }

    public override void Flush()
    {
        foreach (var writer in _writers)
            writer.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var writer in _writers)
                writer.Flush();
        }
        base.Dispose(disposing);
    }
}
