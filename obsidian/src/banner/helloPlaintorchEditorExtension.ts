import { RangeSetBuilder, StateField, type Extension, type Text } from "@codemirror/state";
import { Decoration, type DecorationSet, EditorView } from "@codemirror/view";

import { HelloPlaintorchWidget } from "./createHelloPlaintorchElement";

export function createHelloPlaintorchEditorExtension(): Extension {
  const field = StateField.define<DecorationSet>({
    create(state) {
      return createDecorations(state.doc);
    },
    update(decorations, transaction) {
      if (transaction.docChanged) {
        return createDecorations(transaction.newDoc);
      }

      return decorations.map(transaction.changes);
    },
    provide(stateField) {
      return EditorView.decorations.from(stateField);
    }
  });

  return [field];
}

function createDecorations(doc: Text): DecorationSet {
  const builder = new RangeSetBuilder<Decoration>();
  const insertPosition = getInsertPosition(doc.toString());

  builder.add(
    insertPosition,
    insertPosition,
    Decoration.widget({
      widget: new HelloPlaintorchWidget(),
      block: true,
      side: -1
    })
  );

  return builder.finish();
}

function getInsertPosition(content: string): number {
  if (!content.startsWith("---")) {
    return 0;
  }

  const match = content.match(/^---\r?\n[\s\S]*?\r?\n---(?:\r?\n)?/);
  return match ? match[0].length : 0;
}
