import { X } from "lucide-react";
import type { RefObject } from "react";
import type { TrailerResponse } from "@/api/types";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { trailerSrc } from "./trailerSrc";

interface Props {
  trailer: TrailerResponse;
  title: string;
  open: boolean;
  onClose: () => void;
  /** Where focus goes on close. Radix only restores focus to a Trigger, and this dialog is opened by code. */
  returnFocusTo?: RefObject<HTMLElement | null>;
}

// D16: a cross-origin iframe swallows key presses once clicked, so Escape cannot be the only way out.
// Close is the first thing in the dialog and takes focus. Closing returns focus to the button that opened it.
export function TrailerPlayer({ trailer, title, open, onClose, returnFocusTo }: Props) {
  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent
        showCloseButton={false}
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          returnFocusTo?.current?.focus();
        }}
        className="!fixed !inset-0 !top-0 !left-0 flex !h-screen !w-screen !max-w-none !translate-x-0 !translate-y-0 flex-col gap-0 rounded-none border-0 bg-black p-0"
      >
        <DialogTitle className="sr-only">{`${title}: trailer`}</DialogTitle>
        <DialogDescription className="sr-only">Press Close to return.</DialogDescription>
        <div className="flex justify-end p-4">
          <Button autoFocus variant="secondary" size="lg" onClick={onClose}>
            <X aria-hidden="true" /> Close
          </Button>
        </div>
        <iframe
          title={`${title}: trailer`}
          src={trailerSrc(trailer, { background: false })}
          allow="autoplay; encrypted-media; fullscreen"
          className="w-full flex-1 border-0"
        />
      </DialogContent>
    </Dialog>
  );
}
