using MiloBot.Brain;
using MiloBot.Win32;

namespace MiloBot;

public partial class App
{
    bool pressed, dragging, tugging;
    double pressX, pressY;
    Toy draggedToy, tugToy;
    ToyMouse toyMouse;
    HomeMouse homeMouse;
    CritterMouse critterMouse;

    public void MouseDown()
    {
        Native.GetCursorPos(out var p);
        pressed = true;
        dragging = false;
        pressX = p.X;
        pressY = p.Y;
        var held = toys.InMouth;
        if (held != null && held.Fetchable && !milo.Asleep)
        {
            var (mx, my) = milo.MouthPoint();
            if (Math.Abs(p.X - mx) < 12 * scale && Math.Abs(p.Y - my) < 12 * scale)
            {
                tugging = true;
                tugToy = held;
                milo.StartTug(held);
            }
        }
    }

    public void MouseMove()
    {
        if (!pressed || dragging || tugging) return;
        Native.GetCursorPos(out var p);
        if (Math.Abs(p.X - pressX) + Math.Abs(p.Y - pressY) > 6)
        {
            dragging = true;
            milo.OnGrab();
        }
    }

    public void MouseUp()
    {
        if (!pressed) return;
        pressed = false;
        if (tugging)
        {
            tugging = false;
            milo.EndTugByUser();
            return;
        }
        if (draggedToy != null && tugToy == draggedToy)
        {
            tugToy = null;
            new ToyMouse(this).ReleaseDragged();
            return;
        }
        if (dragging) EndDrag(true);
        else milo.OnClick();
    }

    void EndDrag(bool throwIt)
    {
        dragging = false;
        pressed = false;
        milo.OnRelease(throwIt ? world.CursorVX : 0, throwIt ? world.CursorVY : 0);
    }

    public void RightClick() => ShowMenu();

    public void CaptureLost()
    {
        if (tugging) { tugging = false; milo.EndTugByUser(); }
        if (draggedToy != null && tugToy == draggedToy) { tugToy = null; new ToyMouse(this).ReleaseDragged(); }
        if (dragging) EndDrag(true);
        pressed = false;
    }

    void UpdateTug()
    {
        if (!tugging) return;
        if (milo.Current is not Tug t) { tugging = false; return; }
        if (t.Outcome == 2 && tugToy != null)
        {
            tugging = false;
            tugToy.InMouth = false;
            tugToy.HeldByUser = true;
            tugToy.Body.Detach();
            draggedToy = tugToy;
        }
        else if (t.Outcome == 1) tugging = false;
    }

    class ToyMouse : IMouseTarget
    {
        readonly App app;
        public ToyMouse(App app) { this.app = app; }

        public void MouseDown()
        {
            Native.GetCursorPos(out var p);
            var t = app.toys.Items.Where(x => !x.InMouth && !x.Hidden).OrderBy(x => Math.Abs(x.Body.X - p.X) + Math.Abs(x.Body.Y - 6 * app.scale - p.Y)).FirstOrDefault();
            if (t == null) return;
            app.draggedToy = t;
            t.HeldByUser = true;
            t.Body.Detach();
            t.Body.VX = t.Body.VY = 0;
            if (t.Kind == ToyKind.Ball) app.sound.Play("squeak", app.Now);
        }

        public void MouseMove() { }

        public void MouseUp() => ReleaseDragged();

        public void RightClick() => app.ShowMenu();

        public void CaptureLost() => ReleaseDragged();

        public void ReleaseDragged()
        {
            var t = app.draggedToy;
            if (t == null) return;
            app.draggedToy = null;
            t.HeldByUser = false;
            double max = 1600 * app.scale / 3.0;
            double vx = Math.Clamp(app.world.CursorVX, -max, max), vy = Math.Clamp(app.world.CursorVY, -max, max);
            t.Body.VX = vx;
            t.Body.VY = vy;
            t.Body.Detach();
            bool thrown = Math.Abs(vx) + Math.Abs(vy) > 150 || t.Kind == ToyKind.Ball;
            if (t.Fetchable) t.ThrownAt = app.Now;
            app.milo.OnToy(t, thrown);
        }
    }

    class HomeMouse : IMouseTarget
    {
        readonly App app;
        public HomeMouse(App app) { this.app = app; }

        public void MouseDown()
        {
            Native.GetCursorPos(out var p);
            if (Math.Abs(p.X - app.home.BowlX) < 14 * app.scale) app.FillBowl();
        }

        public void MouseMove() { }
        public void MouseUp() { }
        public void RightClick() => app.ShowMenu();
        public void CaptureLost() { }
    }

    class CritterMouse : IMouseTarget
    {
        readonly App app;
        public CritterMouse(App app) { this.app = app; }

        public void MouseDown()
        {
            Native.GetCursorPos(out var p);
            var c = app.critters.Items.OrderBy(x => Math.Abs(x.X - p.X) + Math.Abs(x.Y - p.Y)).FirstOrDefault();
            if (c != null) app.critters.Scare(c, app.world);
            if (c?.Kind == CritterKind.Bird) app.sound.Play("chirp", app.Now);
        }

        public void MouseMove() { }
        public void MouseUp() { }
        public void RightClick() => app.ShowMenu();
        public void CaptureLost() { }
    }

    void FillBowl()
    {
        if (save.BowlFood >= 0.99) return;
        save.BowlFood = 1;
        sound.Play("crunch", Now);
        homeKey = "";
        if (!milo.Asleep)
        {
            milo.Face(home.BowlX, 0);
            milo.Alert(1.5);
            milo.Emote("food", 1.5);
        }
        milo.Memory.Cool("meal", 1, Now);
        if (save.Hunger > 0.4 && !milo.Asleep && (milo.Current == null || milo.Current.Priority < 2)) milo.Set(new Meal());
    }
}
