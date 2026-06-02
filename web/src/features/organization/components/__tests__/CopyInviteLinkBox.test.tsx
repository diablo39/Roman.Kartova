import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { CopyInviteLinkBox } from "../CopyInviteLinkBox";

const URL = "https://kartova.io/i/abc123";
const EMAIL = "alice@example.com";

/**
 * Install a fresh clipboard mock on `navigator` for the duration of the test.
 * jsdom's default `navigator.clipboard` is undefined, so we add it with a
 * configurable descriptor we can swap per test.
 */
function installClipboard(writeText: ReturnType<typeof vi.fn>) {
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    writable: true,
    value: { writeText },
  });
}

describe("CopyInviteLinkBox", () => {
  let writeText: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    writeText = vi.fn().mockResolvedValue(undefined);
    installClipboard(writeText);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("renders the email and the URL value", () => {
    render(<CopyInviteLinkBox url={URL} email={EMAIL} />);
    expect(screen.getByText(new RegExp(EMAIL))).toBeInTheDocument();
    const input = screen.getByLabelText(/invite link/i) as HTMLInputElement;
    expect(input.value).toBe(URL);
  });

  it("clicking Copy writes the URL to the clipboard", async () => {
    render(<CopyInviteLinkBox url={URL} email={EMAIL} />);

    await userEvent.click(screen.getByRole("button", { name: /copy invite link/i }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(URL));
  });

  it("shows 'Copied!' after a successful copy then reverts after the timeout", async () => {
    // Real timers throughout — `waitFor` polls jsdom until the 2s setTimeout
    // resolves naturally. Cheap because the test renders no expensive tree.
    render(<CopyInviteLinkBox url={URL} email={EMAIL} />);

    await userEvent.click(screen.getByRole("button", { name: /copy invite link/i }));

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /copied!/i })).toBeInTheDocument(),
    );

    // Wait up to ~3s for the component to revert. The `Copy invite link` label
    // returning means the 2s setTimeout fired and setCopied(false) ran.
    await waitFor(
      () =>
        expect(
          screen.getByRole("button", { name: /copy invite link/i }),
        ).toBeInTheDocument(),
      { timeout: 3500 },
    );
  });

  it("falls back to an inline error when the clipboard API rejects", async () => {
    // Re-install with a rejecting writeText so the failure path is exercised.
    const reject = vi.fn().mockRejectedValue(new Error("permission denied"));
    installClipboard(reject);

    render(<CopyInviteLinkBox url={URL} email={EMAIL} />);

    await userEvent.click(screen.getByRole("button", { name: /copy invite link/i }));

    await waitFor(() =>
      expect(
        screen.getByText(/could not copy to clipboard/i),
      ).toBeInTheDocument(),
    );
  });
});
