// An operator/user record may lack a name or email (KC profile gaps); never render a blank.
export const orDash = (value: string | undefined) => (value && value.trim() ? value : "—");
