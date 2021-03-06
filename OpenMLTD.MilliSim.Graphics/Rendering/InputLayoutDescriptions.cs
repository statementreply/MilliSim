using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace OpenMLTD.MilliSim.Graphics.Rendering {
    public static class InputLayoutDescriptions {

        public static readonly InputElement[] PosNormTexTan = {
            new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
            new InputElement("NORMAL", 0, Format.R32G32B32_Float, InputElement.AppendAligned, 0, InputClassification.PerVertexData, 0),
            new InputElement("TEXCOORD", 0, Format.R32G32_Float, InputElement.AppendAligned, 0, InputClassification.PerVertexData, 0),
            new InputElement("TANGENT", 0, Format.R32G32B32_Float, InputElement.AppendAligned, 0, InputClassification.PerVertexData,0 )
        };

    }
}
