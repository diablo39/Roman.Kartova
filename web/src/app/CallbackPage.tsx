import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "react-oidc-context";
import { toast } from "sonner";

export function CallbackPage() {
  const auth = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    if (auth.isAuthenticated) {
      navigate("/", { replace: true });
    } else if (auth.error) {
      console.error("OIDC callback failed:", auth.error);
      toast.error("Sign-in failed", { description: auth.error.message });
      navigate("/", { replace: true });
    }
  }, [auth.isAuthenticated, auth.error, navigate]);

  return <div className="p-8 text-sm">Completing sign-in…</div>;
}
