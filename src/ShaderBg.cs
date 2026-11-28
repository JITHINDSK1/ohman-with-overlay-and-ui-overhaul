// SPDX-License-Identifier: GPL-3.0-or-later
// GPU-accelerated "Mesh drift" (blobs) shader background matching the 21st.dev shader specification.
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Ohman {
    public sealed class MeshDriftEffect : ShaderEffect {
        static readonly PixelShader SharedShader = LoadPixelShader();

        static PixelShader LoadPixelShader() {
            var ps = new PixelShader { ShaderRenderMode = ShaderRenderMode.Auto };
            var asm = Assembly.GetExecutingAssembly();
            Stream st = asm.GetManifestResourceStream("OMI.MeshDrift.ps")
                     ?? asm.GetManifestResourceStream("Ohman.MeshDrift.ps");
            if (st != null) {
                using (st) {
                    var ms = new MemoryStream();
                    st.CopyTo(ms);
                    ms.Position = 0;
                    ps.SetStreamSource(ms);
                }
            }
            return ps;
        }

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty("Input", typeof(MeshDriftEffect), 0);

        public static readonly DependencyProperty ResolutionProperty =
            DependencyProperty.Register("Resolution", typeof(Size), typeof(MeshDriftEffect),
                new UIPropertyMetadata(new Size(1042, 720), PixelShaderConstantCallback(0)));

        public static readonly DependencyProperty TimeProperty =
            DependencyProperty.Register("Time", typeof(double), typeof(MeshDriftEffect),
                new UIPropertyMetadata(0.0, PixelShaderConstantCallback(1)));

        public Brush Input {
            get { return (Brush)GetValue(InputProperty); }
            set { SetValue(InputProperty, value); }
        }

        public Size Resolution {
            get { return (Size)GetValue(ResolutionProperty); }
            set { SetValue(ResolutionProperty, value); }
        }

        public double Time {
            get { return (double)GetValue(TimeProperty); }
            set { SetValue(TimeProperty, value); }
        }

        public MeshDriftEffect() {
            PixelShader = SharedShader;
            UpdateShaderValue(InputProperty);
            UpdateShaderValue(ResolutionProperty);
            UpdateShaderValue(TimeProperty);
        }
    }
}
