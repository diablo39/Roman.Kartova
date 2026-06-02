import {
  API_BASE_URL,
  createAnonymousApiClient,
} from "@/features/catalog/api/client";

/** No Authorization header — the invitee has no session; the token is the only credential. */
export const anonymousApiClient = createAnonymousApiClient(API_BASE_URL);
