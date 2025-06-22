using UnityEngine;
using nadena.dev.ndmf;
using System.Reflection;

[assembly: ExportsPlugin(typeof(PeanutTools_VRC3CVR.NDMF.VRC3CVRNDMFPlugin))]

namespace PeanutTools_VRC3CVR.NDMF
{
    internal class VRC3CVRNDMFPlugin : Plugin<VRC3CVRNDMFPlugin>
    {
        public override string DisplayName => "VRC3CVR NDMF Plugin";
        public override string QualifiedName => "VRC3CVR.NDMF";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("nadena.dev.modular-avatar")
                .AfterPlugin("com.anatawa12.avatar-optimizer")
                .AfterPlugin("com.hhotatea.avatar_pose_library.editor.AutoThumbnailPlugin")
                .AfterPlugin("net.raitichan.int-parameter-compressor")
                .Run(QualifiedName, Run);
        }

        void Run(BuildContext ctx)
        {
            var vrc3cvrNdmf = ctx.AvatarRootObject.GetComponent<VRC3CVRNDMF>();
            if (vrc3cvrNdmf == null)
            {
                return;
            }
            var config = new VRC3CVRConvertConfig();
            config.CopyFrom(vrc3cvrNdmf.convertConfig);
            Object.DestroyImmediate(vrc3cvrNdmf);
            config.vrcAvatarDescriptor = ctx.AvatarDescriptor;
            config.shouldCloneAvatar = false;
            config.saveAssets = false;

            var vrc3cvrCore = Assembly.Load("Assembly-CSharp-Editor").GetType("VRC3CVRCore");
            var fromConfig = vrc3cvrCore.GetMethod("FromConfig", BindingFlags.Public | BindingFlags.Static);
            var convert = vrc3cvrCore.GetMethod("Convert", BindingFlags.Public | BindingFlags.Instance);
            var vrc3cvr = fromConfig.Invoke(null, new object[] { config });
            convert.Invoke(vrc3cvr, null);
        }
    }
}
