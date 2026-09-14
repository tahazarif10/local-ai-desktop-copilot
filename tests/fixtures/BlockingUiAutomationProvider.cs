using System.Drawing;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using System.Windows.Forms;

namespace LocalCopilotM34Fixture
{
    // This is a deliberately tiny HWND-backed server-side UI Automation
    // provider. It responds to UIA's WM_GETOBJECT request with an explicit
    // IRawElementProviderSimple so the Name read below is the actual
    // cross-process provider transaction under measurement.
    public sealed class BlockingNameButton :
        Control,
        IRawElementProviderSimple
    {
        private const int WmGetObject = 0x003D;

        private const int BoundingRectanglePropertyId = 30001;
        private const int ControlTypePropertyId = 30003;
        private const int NamePropertyId = 30005;
        private const int HasKeyboardFocusPropertyId = 30008;
        private const int IsKeyboardFocusablePropertyId = 30009;
        private const int IsEnabledPropertyId = 30010;
        private const int IsControlElementPropertyId = 30016;
        private const int IsContentElementPropertyId = 30017;
        private const int IsPasswordPropertyId = 30019;
        private const int IsOffscreenPropertyId = 30022;
        private const int IsDialogPropertyId = 30174;

        private const int ButtonControlTypeId = 50000;

        private readonly string
            _gateName;

        private readonly string
            _enteredName;

        private readonly string
            _releaseName;

        private readonly string
            _sentinel;

        public BlockingNameButton(
            string gateName,
            string enteredName,
            string releaseName,
            string sentinel)
        {
            _gateName = gateName;
            _enteredName = enteredName;
            _releaseName = releaseName;
            _sentinel = sentinel;

            TabStop = true;
            Size = new Size(260, 40);

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
        }

        public override Size GetPreferredSize(
            Size proposedSize)
        {
            return new Size(260, 40);
        }

        protected override void WndProc(
            ref Message message)
        {
            if (message.Msg == WmGetObject &&
                message.LParam.ToInt32() ==
                    AutomationInteropProvider.RootObjectId)
            {
                message.Result =
                    AutomationInteropProvider
                        .ReturnRawElementProvider(
                            Handle,
                            message.WParam,
                            message.LParam,
                            this);

                return;
            }

            base.WndProc(ref message);
        }

        protected override void OnPaint(
            PaintEventArgs e)
        {
            base.OnPaint(e);

            ControlPaint.DrawButton(
                e.Graphics,
                ClientRectangle,
                ButtonState.Normal);

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                ForeColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine);
        }

        ProviderOptions
            IRawElementProviderSimple.ProviderOptions
        {
            get
            {
                return ProviderOptions.ServerSideProvider;
            }
        }

        IRawElementProviderSimple
            IRawElementProviderSimple.HostRawElementProvider
        {
            get
            {
                return AutomationInteropProvider
                    .HostProviderFromHandle(
                        Handle);
            }
        }

        object
            IRawElementProviderSimple.GetPatternProvider(
                int patternId)
        {
            return null;
        }

        object
            IRawElementProviderSimple.GetPropertyValue(
                int propertyId)
        {
            switch (propertyId)
            {
                case NamePropertyId:
                    return ReadBlockingName();

                case ControlTypePropertyId:
                    return ButtonControlTypeId;

                case IsControlElementPropertyId:
                case IsContentElementPropertyId:
                case IsEnabledPropertyId:
                case IsKeyboardFocusablePropertyId:
                    return true;

                case HasKeyboardFocusPropertyId:
                    return Focused;

                case IsPasswordPropertyId:
                case IsOffscreenPropertyId:
                case IsDialogPropertyId:
                    return false;

                // Let the host HWND provider supply geometry and any
                // platform-owned values that this measurement does not need.
                case BoundingRectanglePropertyId:
                default:
                    return null;
            }
        }

        private string ReadBlockingName()
        {
            using (EventWaitHandle gate =
                EventWaitHandle.OpenExisting(
                    _gateName))
            {
                if (!gate.WaitOne(0))
                {
                    return
                        "Controlled blocking provider";
                }
            }

            using (EventWaitHandle entered =
                EventWaitHandle.OpenExisting(
                    _enteredName))
            {
                entered.Set();
            }

            using (EventWaitHandle release =
                EventWaitHandle.OpenExisting(
                    _releaseName))
            {
                release.WaitOne();
            }

            return _sentinel;
        }
    }
}
