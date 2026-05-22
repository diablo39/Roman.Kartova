import { z } from "zod";

export const sunsetDateField = z
  .string()
  .min(1, "Sunset date is required.")
  .refine(
    (v) => {
      const d = new Date(v);
      return !Number.isNaN(d.getTime()) && d.getTime() > Date.now();
    },
    { message: "Sunset date must be in the future." }
  );
