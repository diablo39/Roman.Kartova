import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "react-oidc-context";

export function CallbackPage() {
  const auth = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    if (auth.isAuthenticated) {
      navigate("/", { replace: true });
    } else if (auth.error) {
      navigate("/", { replace: true });
    }
  }, [auth.isAuthenticated, auth.error, navigate]);

  return <div className="p-8 text-sm">Completing sign-in…</div>;
}
