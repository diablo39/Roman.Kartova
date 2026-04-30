import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";

import {
  registerApplicationSchema,
  type RegisterApplicationInput,
} from "@/features/catalog/schemas/registerApplication";
import { useRegisterApplication } from "@/features/catalog/api/applications";
import { applyProblemDetailsToForm } from "@/shared/forms/problemDetails";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function RegisterApplicationDialog({ open, onOpenChange }: Props) {
  const user = useCurrentUser();
  const mutation = useRegisterApplication();
  const form = useForm<RegisterApplicationInput>({
    resolver: zodResolver(registerApplicationSchema),
    defaultValues: { name: "", displayName: "", description: "" },
  });

  // Reset form when the dialog closes (so re-opening starts clean).
  useEffect(() => {
    if (!open) {
      form.reset({ name: "", displayName: "", description: "" });
    }
  }, [open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await mutation.mutateAsync(values);
      toast.success("Application registered");
      onOpenChange(false);
    } catch (err) {
      const handled = applyProblemDetailsToForm(err as never, form.setError as never);
      if (!handled) {
        const detail =
          (err as { detail?: string }).detail ??
          (err as { title?: string }).title ??
          "Failed to register application";
        toast.error(detail);
      }
    }
  });

  const initials =
    user?.displayName
      ?.split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((p) => p[0]?.toUpperCase())
      .join("") ?? "?";

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-[560px]">
        <DialogHeader>
          <DialogTitle>Register Application</DialogTitle>
          <DialogDescription>Add a new application to your catalog</DialogDescription>
        </DialogHeader>

        <Form {...form}>
          <form onSubmit={onSubmit} className="space-y-5">
            <FormField
              control={form.control}
              name="name"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Name *</FormLabel>
                  <FormControl>
                    <Input placeholder="payment-gateway" {...field} />
                  </FormControl>
                  <FormDescription>Lowercase, kebab-case. Used in URLs and CLI.</FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="displayName"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Display Name *</FormLabel>
                  <FormControl>
                    <Input placeholder="Payment Gateway" {...field} />
                  </FormControl>
                  <FormDescription>Human-friendly name shown in UI.</FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="description"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Description *</FormLabel>
                  <FormControl>
                    <Textarea rows={3} placeholder="Short summary..." {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <div className="grid grid-cols-2 gap-4">
              <div>
                <p className="text-xs uppercase tracking-wide text-muted-foreground">Owner</p>
                <div className="mt-1 inline-flex items-center gap-2 rounded-md border border-border bg-muted/40 px-2 py-1.5">
                  <Avatar className="h-6 w-6">
                    <AvatarFallback className="text-[10px]">{initials}</AvatarFallback>
                  </Avatar>
                  <div className="min-w-0">
                    <div className="text-sm font-medium truncate">{user?.displayName ?? "—"}</div>
                    <div className="text-xs text-muted-foreground truncate">{user?.email ?? ""}</div>
                  </div>
                </div>
              </div>
              <div>
                <p className="text-xs uppercase tracking-wide text-muted-foreground">Lifecycle</p>
                <Badge className="mt-1 bg-emerald-600 text-white hover:bg-emerald-700">Active</Badge>
              </div>
            </div>

            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={mutation.isPending}>
                {mutation.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                Register Application
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
