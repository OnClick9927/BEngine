        // BEngine: AttachThreadInput may expose another process's active HWND.
        // Its GLFW property is a pointer in that process, or another library
        // instance. Only use windows owned by this GLFW instance.
        for (window = _glfw.windowListHead; window; window = window->next)
        {
            if (window->win32.handle == handle)
                break;
        }
