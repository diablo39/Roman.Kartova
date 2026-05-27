import { z } from "zod";

export const addTeamMemberSchema = z.object({
  userId: z.string().uuid("Must be a valid UUID"),
  role: z.enum(["Admin", "Member"]),
});

export type AddTeamMemberInput = z.infer<typeof addTeamMemberSchema>;
