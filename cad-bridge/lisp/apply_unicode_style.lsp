;; BWUNICODE: apply Bimwright Unicode text formatting to an explicit selection.
;; Programmatic entry: (bw:apply-unicode '("ABC" "DEF") "C:/.../OpenSans-CondensedLight.ttf")
;; dwg_run_lisp arguments_json: [["ABC","DEF"],"C:/.../OpenSans-CondensedLight.ttf"]
;; Handles must be a nonempty exact list (maximum 1000), never an implicit fallback.
;; An empty font path searches already existing local fonts; nothing is downloaded.
;; Heights are drawing units: SHX *0.60, TrueType *0.90, unknown *0.82.
;; Existing Unicode style/face/font keeps its height; nonpositive height -> 2.5.
;; Text/MText/single-line attributes and ordinary text MLeaders are supported.
;; A selected block changes its own layer and styles its editable attributes;
;; attribute layers are retained unless their handles are also explicitly given.
;; Colors, including explicit colors, are retained. No forced ByLayer conversion.
;; Full preflight precedes writes. Unsupported objects, fields, inline font/height/
;; color/width/spacing overrides,
;; annotative/xref/nested block references/dynamic blocks, multiline/constant attributes and locked
;; or hidden layers are refused. Existing incompatible target style/layer is refused.
;; One Undo group; property restoration is attempted inside the owned open group
;; on error, not guaranteed. If that group was lost/closed, inspect and Undo manually.
;; No geometry setters, saves, external loads, runtime copies or font installation.
;; Loading this file defines functions only; run BWUNICODE to select and apply.
;; Explicit text handles inside ordinary block definitions are supported;
;; this changes every reference to that definition, not one selected instance.
;; Return value: alist with ok, phase, partial_changes, rollback_verified, counts,
;; font_path and per-object records. MCP transports its printed Lisp representation.
;; At most 10 object records are returned; object_count/objects_reported/
;; objects_complete describe report coverage. Every target is verified before success.
;; MCP completed means execution ended: inspect ok/partial_changes/rollback_verified.
;; rollback_verified concerns recorded target properties/resources, not the whole DWG.
;; Native validation in AutoCAD 2026/2027 is still required before production use.
;; Autodesk ActiveX references (Windows):
;; FontFile / SetFont: GUID-DD2E1EB8-3BDE-46B6-ADC4-636234CCB3E7,
;; GUID-DB668114-2395-43C6-858C-2F2514C4BF46 (AutoCAD-ActiveX-Reference).
;; GetFont: GUID-7070FBED-AD5F-47EB-BB9A-5CD25B76DCC8.
;; MLeader TextStyleName/TextHeight: MLeader Object,
;; GUID-95FD33C2-DF78-4014-933C-9AC124E6A35D (same reference).

(defun bwuc:fail (message)
  (setq bwuc:failure message)
  (exit))

(defun bwuc:eq (a b)
  (and (= (type a) 'STR) (= (type b) 'STR) (= (strcase a) (strcase b))))

(defun bwuc:value (key data) (cdr (assoc key data)))

(defun bwuc:font-info (style / face bold italic charset pitch)
  (vla-GetFont style 'face 'bold 'italic 'charset 'pitch)
  (list face bold italic charset pitch))

(defun bwuc:font-base (path)
  (if (and (= (type path) 'STR) (/= path ""))
    (strcase (strcat (vl-filename-base path) (cond ((vl-filename-extension path)) (""))))
    ""))

(defun bwuc:absolute-p (path)
  (and (= (type path) 'STR)
       (or (and (> (strlen path) 2) (= (substr path 2 1) ":")
                (member (substr path 3 1) '("/" "\\")))
           (and (> (strlen path) 2) (= (substr path 1 2) "\\\\")))))

(defun bwuc:find-font (path / found base)
  (if (/= (type path) 'STR) (bwuc:fail "font_path must be a string; use an empty string to search local fonts"))
  (setq base "OpenSans-CondensedLight.ttf")
  (if (/= path "")
    (progn
      (if (not (bwuc:absolute-p path)) (bwuc:fail "font_path must be an absolute path"))
      (setq found (findfile path)))
    (setq found
      (or (findfile base)
          (and (getenv "WINDIR") (findfile (strcat (getenv "WINDIR") "/Fonts/" base)))
          (and (getenv "LOCALAPPDATA")
               (findfile (strcat (getenv "LOCALAPPDATA") "/Bimwright/Fonts/" base))))))
  (if (not found)
    (bwuc:fail "OpenSans-CondensedLight.ttf was not found; pass the existing local checkout font path"))
  (if (not (bwuc:eq (bwuc:font-base found) base))
    (bwuc:fail "Only the existing OpenSans-CondensedLight.ttf font is supported"))
  found)

(defun bwuc:extension-entry (entity name / data dictionary)
  (setq data (entget entity) dictionary (cdr (assoc 360 data)))
  (and dictionary (dictsearch dictionary name)))

(defun bwuc:plain-object (entity label / data)
  (setq data (entget entity '("AcadAnnotative")))
  ;; Reject any annotation metadata/context instead of guessing the active scale.
  (if (or (assoc -3 data) (bwuc:extension-entry entity "AcDbContextDataManager")
          (bwuc:extension-entry entity "ACAD_CONTEXTDATAMANAGER")
          (bwuc:extension-entry entity "ACAD_ANNOTATIVE"))
    (bwuc:fail (strcat label ": annotative/context data is not supported")))
  (if (bwuc:extension-entry entity "ACAD_FIELD")
    (bwuc:fail (strcat label ": AutoCAD fields are not supported")))
  data)

(defun bwuc:layer-check (name / layer)
  (if (vl-string-search "|" name) (bwuc:fail (strcat "Xref-dependent layer: " name)))
  (setq layer (vla-Item (vla-get-Layers bwuc:doc) name))
  (if (or (= (vla-get-Lock layer) :vlax-true)
          (= (vla-get-Freeze layer) :vlax-true)
          (= (vla-get-LayerOn layer) :vlax-false))
    (bwuc:fail (strcat "Layer is locked, frozen or off: " name)))
  layer)

(defun bwuc:owner-record (entity layouts-only / data owner block name)
  (setq data (entget entity) owner (cdr (assoc 330 data)))
  (if (not owner) (bwuc:fail "Object has no readable owner"))
  (setq block (vlax-ename->vla-object owner))
  (if (/= (vla-get-ObjectName block) "AcDbBlockTableRecord")
    (bwuc:fail "Object must belong directly to a block record"))
  (if (/= (vla-get-IsLayout block) :vlax-true)
    (progn
      (if layouts-only (bwuc:fail "Nested block references are not supported"))
      (setq name (vla-get-Name block))
      ;; IsDynamicBlock applies to AcadBlock as well as BlockReference (Autodesk
      ;; ActiveX GUID-7EF9313C-D017-4698-A421-46DF696C1B4E). No private DXF guesses.
      (if (or (= (vla-get-IsXRef block) :vlax-true)
              (= (vla-get-IsDynamicBlock block) :vlax-true)
              (= (substr name 1 1) "*") (vl-string-search "|" name))
        (bwuc:fail "Xref, anonymous or dynamic block definitions are not supported"))
      (bwuc:plain-object owner (strcat "Block definition " name))))
  block)

(defun bwuc:layout-owner (entity) (bwuc:owner-record entity T))
(defun bwuc:text-owner (entity) (bwuc:owner-record entity nil))

(defun bwuc:block-check (entity / object definition name)
  (bwuc:plain-object entity "Block")
  (bwuc:layout-owner entity)
  (setq object (vlax-ename->vla-object entity) name (vla-get-Name object)
        definition (vla-Item (vla-get-Blocks bwuc:doc) name))
  (if (or (= (vla-get-IsXRef definition) :vlax-true) (vl-string-search "|" name))
    (bwuc:fail "Xref blocks are not supported"))
  (bwuc:plain-object (vlax-vla-object->ename definition) "Block definition")
  (if (and (vlax-property-available-p object 'IsDynamicBlock)
           (= (vla-get-IsDynamicBlock object) :vlax-true))
    (bwuc:fail "Dynamic blocks are not supported"))
  (if (not (vlax-write-enabled-p object)) (bwuc:fail "Block is not writable"))
  (bwuc:layer-check (vla-get-Layer object))
  object)

(defun bwuc:text-check (text label / index char next)
  (if (vl-string-search "%<" text) (bwuc:fail (strcat label ": fields are not supported")))
  (setq index 1)
  (while (<= index (strlen text))
    (setq char (substr text index 1))
    (if (and (= char "\\") (< index (strlen text)))
      (progn
        (setq next (substr text (1+ index) 1))
        (if (member next '("F" "f" "H" "h" "C" "c" "W" "w" "T" "t" "Q" "q" "A" "a"))
          (bwuc:fail (strcat label ": inline font/height/color/width/spacing formatting is not supported")))
        (setq index (1+ index))))
    (setq index (1+ index))))

(defun bwuc:geometry (entity / data kind codes result item)
  (setq data (entget entity) kind (cdr (assoc 0 data)))
  ;; Compare anchor/alignment/width/rotation and leader vertices after native writes.
  ;; Calculated text extents are deliberately not geometry invariants.
  (setq codes
    (cond ((= kind "MTEXT") '(10 11 41 50 71 72 73 210))
          ((= kind "MULTILEADER") '(10 11 12 13 14 15 16 17 18 50 110 111 112 210))
          ((= kind "INSERT") '(2 10 41 42 43 50 66 70 71 210))
          (T '(10 11 41 50 51 71 72 73 74 210))))
  (foreach item data
    (if (or (member (car item) codes) (member (car item) '(62 420 430)))
      (setq result (cons item result))))
  (reverse result))

(defun bwuc:style-check (name / entity style)
  (setq entity (tblobjname "STYLE" name))
  (if (or (not entity) (vl-string-search "|" name))
    (bwuc:fail (strcat "Missing or xref-dependent text style: " name)))
  (bwuc:plain-object entity (strcat "Style " name))
  (setq style (vla-Item (vla-get-TextStyles bwuc:doc) name))
  style)

(defun bwuc:scale (name / style font info extension)
  (setq style (bwuc:style-check name) font (vla-get-FontFile style)
        info (vl-catch-all-apply 'bwuc:font-info (list style))
        extension (vl-filename-extension font))
  (if (vl-catch-all-error-p info) (setq info nil))
  (cond
    ((or (bwuc:eq name "Bimwright_Unicode")
         (bwuc:eq (car info) "Open Sans Condensed Light")
         (bwuc:eq (bwuc:font-base font) "OpenSans-CondensedLight.ttf")) 1.0)
    ((bwuc:eq extension ".shx") 0.60)
    ((or (bwuc:eq extension ".ttf") (bwuc:eq extension ".otf")
         (and (= (type (car info)) 'STR) (/= (vl-string-trim " " (car info)) ""))) 0.90)
    (T 0.82)))

(defun bwuc:target-style-compatible (style / info)
  (setq info (bwuc:font-info style))
  ;; AutoCAD may normalize charset/pitch; compare the actual font and appearance.
  (and (bwuc:eq (bwuc:font-base (vla-get-FontFile style)) "OpenSans-CondensedLight.ttf")
       (findfile (vla-get-FontFile style))
       (bwuc:eq (car info) "Open Sans Condensed Light")
       (= (cadr info) :vlax-false) (= (caddr info) :vlax-false)
       (= (vla-get-Height style) 0.0) (= (vla-get-Width style) 1.0)
       (= (vla-get-ObliqueAngle style) 0.0) (= (vla-get-TextGenerationFlag style) 0)
       (= (vla-get-BigFontFile style) "")))

(defun bwuc:shared-preflight (/ entity style layer)
  (if (= (getvar "WRITESTAT") 0) (bwuc:fail "The active drawing is read-only"))
  (if (or (= 0 (logand 1 (getvar "UNDOCTL")))
          (/= 0 (logand 2 (getvar "UNDOCTL")))
          (/= 0 (logand 8 (getvar "UNDOCTL"))))
    (bwuc:fail "Undo must be enabled in All mode with no open Undo group"))
  (setq bwuc:style-existed (if (tblobjname "STYLE" "Bimwright_Unicode") T nil)
        bwuc:layer-existed (if (tblsearch "LAYER" "layer-bimwright-dwg") T nil))
  (if bwuc:style-existed
    (progn
      (setq style (bwuc:style-check "Bimwright_Unicode"))
      (if (not (bwuc:target-style-compatible style))
        (bwuc:fail "Existing Bimwright_Unicode style is incompatible; it was not overwritten"))))
  (if bwuc:layer-existed
    (progn
      (setq layer (bwuc:layer-check "layer-bimwright-dwg"))
      (if (or (/= (vla-get-Color layer) 7)
              (/= (vla-get-ColorMethod (vla-get-TrueColor layer)) 195))
        (bwuc:fail "Existing layer-bimwright-dwg must already have ACI color 7; it was not overwritten")))))

(defun bwuc:mleader-check (object / dictionary entry entity)
  (if (/= (vla-get-ContentType object) 2) (bwuc:fail "Only MText-content MLeaders are supported"))
  (setq dictionary (dictsearch (namedobjdict) "ACAD_MLEADERSTYLE"))
  (if (or (not dictionary)
          (not (setq entry (dictsearch (cdr (assoc -1 dictionary)) (vla-get-StyleName object)))))
    (bwuc:fail "MLeader style could not be read for annotation preflight"))
  (setq entity (cdr (assoc -1 entry)))
  (bwuc:plain-object entity "MLeader style")
  ;; MLEADERSTYLE DXF group 296 is Is Annotative (Autodesk DXF reference).
  (if (/= 0 (cond ((cdr (assoc 296 entry))) (0)))
    (bwuc:fail "Annotative MLeader styles are not supported"))
  (if (<= (vla-get-TextHeight object) 0.0)
    (bwuc:fail "MLeader has no positive TextHeight; its inner MText fallback is unavailable in ActiveX")))

(defun bwuc:plan-text (entity move-layer / object kind parent styleprop heightprop style height scale text plan old)
  (setq object (vlax-ename->vla-object entity) kind (vla-get-ObjectName object))
  (bwuc:plain-object entity (vla-get-Handle object))
  (if (not (vlax-write-enabled-p object)) (bwuc:fail "Target is not writable"))
  (bwuc:layer-check (vla-get-Layer object))
  (if (= kind "AcDbAttribute")
    (progn
      (setq parent (cdr (assoc 330 (entget entity))))
      (if (or (not parent) (/= (cdr (assoc 0 (entget parent))) "INSERT"))
        (bwuc:fail "Attribute has no supported block reference owner"))
      (bwuc:block-check parent)
      (if (or (/= 0 (logand 2 (cond ((cdr (assoc 70 (entget entity)))) (0))))
              (and (vlax-property-available-p object 'MTextAttribute)
                   (= (vla-get-MTextAttribute object) :vlax-true))
              (and (vlax-property-available-p object 'Constant)
                   (= (vla-get-Constant object) :vlax-true)))
        (bwuc:fail "Multiline or constant attributes are not supported")))
    (bwuc:text-owner entity))
  (if (= kind "AcDbMLeader")
    (progn
      (bwuc:mleader-check object)
      (setq styleprop 'TextStyleName heightprop 'TextHeight))
    (setq styleprop 'StyleName heightprop 'Height))
  (if (or (not (vlax-property-available-p object styleprop T))
          (not (vlax-property-available-p object heightprop T)))
    (bwuc:fail "Native text style/height properties are not writable"))
  (setq text (vla-get-TextString object))
  (bwuc:text-check text (vla-get-Handle object))
  (setq style (vlax-get-property object styleprop) height (vlax-get-property object heightprop)
        scale (bwuc:scale style))
  (setq plan (list (cons 'entity entity) (cons 'object object) (cons 'handle (vla-get-Handle object))
                  (cons 'kind kind) (cons 'style-property styleprop) (cons 'style style)
                  (cons 'height-property heightprop) (cons 'height height)
                  (cons 'target-height (* (if (> height 0.0) height 2.5) scale))
                  (cons 'layer (vla-get-Layer object)) (cons 'move-layer move-layer)
                  (cons 'geometry (bwuc:geometry entity)) (cons 'text text)))
  (if (setq old (assoc entity bwuc:plans))
    ;; Selecting a block and one of its attributes must not scale that attribute twice.
    (if move-layer
      (setq bwuc:plans (subst (cons entity (subst (cons 'move-layer T) (assoc 'move-layer (cdr old)) (cdr old)))
                              old bwuc:plans)))
    (setq bwuc:plans (cons (cons entity plan) bwuc:plans)))
  (if (> (length bwuc:plans) 1000) (bwuc:fail "Expanded targets exceed the 1000 object limit")))

(defun bwuc:plan-target (entity / object kind attributes constants item)
  (setq object (vlax-ename->vla-object entity) kind (vla-get-ObjectName object))
  (cond
    ((member kind '("AcDbText" "AcDbMText" "AcDbAttribute" "AcDbMLeader")) (bwuc:plan-text entity T))
    ((= kind "AcDbBlockReference")
      (bwuc:block-check entity)
      (setq constants (vlax-invoke object 'GetConstantAttributes))
      (if constants (bwuc:fail "A selected block contains constant attribute definitions; complete formatting is unsupported"))
      (setq attributes (vlax-invoke object 'GetAttributes))
      (if (null attributes) (bwuc:fail "A selected block has no editable attributes"))
      (foreach item attributes (bwuc:plan-text (vlax-vla-object->ename item) nil))
      (if (not (assoc entity bwuc:plans))
        (setq bwuc:plans
          (cons (cons entity (list (cons 'entity entity) (cons 'object object)
                                  (cons 'handle (vla-get-Handle object)) (cons 'kind kind)
                                  (cons 'layer (vla-get-Layer object)) (cons 'move-layer T)
                                  (cons 'geometry (bwuc:geometry entity)))) bwuc:plans))))
    (T (bwuc:fail (strcat "Unsupported target type: " kind)))))

(defun bwuc:prepare (handles font-path / handle entity seen count)
  (if (or (not (listp handles)) (null handles)) (bwuc:fail "handles must be a nonempty list"))
  (setq count 0)
  (foreach handle handles
    (setq count (1+ count))
    (if (> count 1000) (bwuc:fail "At most 1000 target handles are supported"))
    (if (or (/= (type handle) 'STR) (= handle "")
            (vl-string-search " " handle)
            (not (vl-every '(lambda (code) (or (and (>= code 48) (<= code 57))
                                             (and (>= code 65) (<= code 70))))
                           (vl-string->list (strcase handle)))))
      (bwuc:fail "Every handle must be a nonempty hexadecimal string"))
    (setq entity (handent handle))
    (if (or (not entity) (not (entget entity))) (bwuc:fail (strcat "Target handle is missing or erased: " handle)))
    (if (not (member entity seen))
      (progn (setq seen (cons entity seen)) (bwuc:plan-target entity))))
  (if (> (length bwuc:plans) 1000) (bwuc:fail "Expanded targets exceed the 1000 object limit"))
  (setq bwuc:font-path (bwuc:find-font font-path))
  (bwuc:shared-preflight)
  (setq bwuc:plans (reverse bwuc:plans))
  (length seen))

(defun bwuc:ensure-resources (/ styles layers)
  (if (not (tblsearch "STYLE" "Bimwright_Unicode"))
    (progn
      (setq styles (vla-get-TextStyles bwuc:doc)
            bwuc:new-style (vla-Add styles "Bimwright_Unicode"))
      (vla-put-FontFile bwuc:new-style bwuc:font-path)
      (vla-SetFont bwuc:new-style "Open Sans Condensed Light" :vlax-false :vlax-false 0 0)
      (vla-put-Height bwuc:new-style 0.0)
      (if (not (bwuc:target-style-compatible bwuc:new-style))
        (bwuc:fail "AutoCAD did not accept the requested Unicode style/font properties"))))
  (if (not (tblsearch "LAYER" "layer-bimwright-dwg"))
    (progn
      (setq layers (vla-get-Layers bwuc:doc)
            bwuc:new-layer (vla-Add layers "layer-bimwright-dwg"))
      (vla-put-Color bwuc:new-layer 7))))

(defun bwuc:verify-plan (plan restored / object style height layer)
  (setq object (bwuc:value 'object plan)
        style (bwuc:value 'style-property plan) height (bwuc:value 'height-property plan)
        layer (if (or restored (not (bwuc:value 'move-layer plan)))
                (bwuc:value 'layer plan) "layer-bimwright-dwg"))
  (and (bwuc:eq (vla-get-Layer object) layer)
       (equal (bwuc:geometry (bwuc:value 'entity plan)) (bwuc:value 'geometry plan) 1e-9)
       (or (not style)
           (and (bwuc:eq (vlax-get-property object style)
                          (if restored (bwuc:value 'style plan) "Bimwright_Unicode"))
                (equal (vlax-get-property object height)
                       (bwuc:value (if restored 'height 'target-height) plan) 1e-9)
                (= (vla-get-TextString object) (bwuc:value 'text plan))))))

(defun bwuc:write-plans (/ entry plan object)
  (bwuc:shared-preflight)
  (foreach entry bwuc:plans
    (if (not (bwuc:verify-plan (cdr entry) T)) (bwuc:fail "A target changed after preflight")))
  (setq bwuc:undo-attempted T)
  (vla-StartUndoMark bwuc:doc)
  (setq bwuc:undo-open T)
  (if (= 0 (logand 8 (getvar "UNDOCTL"))) (bwuc:fail "AutoCAD did not open an Undo group"))
  (setq bwuc:writes-started T)
  (bwuc:ensure-resources)
  (foreach entry bwuc:plans
    (setq plan (cdr entry) object (bwuc:value 'object plan))
    ;; Journal before the first setter so even a partially failing setter is restored.
    (setq bwuc:touched (cons plan bwuc:touched))
    (if (bwuc:value 'style-property plan)
      (progn
        (vlax-put-property object (bwuc:value 'style-property plan) "Bimwright_Unicode")
        (vlax-put-property object (bwuc:value 'height-property plan) (bwuc:value 'target-height plan))))
    (if (bwuc:value 'move-layer plan) (vla-put-Layer object "layer-bimwright-dwg"))
    (vla-Update object)
    (if (not (bwuc:verify-plan plan nil))
      (bwuc:fail (strcat "Readback or geometry verification failed for handle " (bwuc:value 'handle plan)))))
  (vla-Regen bwuc:doc 1)
  (foreach entry bwuc:plans
    (if (not (bwuc:verify-plan (cdr entry) nil)) (bwuc:fail "Final native readback changed after regeneration")))
  (if (not (bwuc:target-style-compatible
             (vla-Item (vla-get-TextStyles bwuc:doc) "Bimwright_Unicode")))
    (bwuc:fail "The target style changed during native updates"))
  (bwuc:layer-check "layer-bimwright-dwg")
  (setq bwuc:resolved-font (findfile (vla-get-FontFile
    (vla-Item (vla-get-TextStyles bwuc:doc) "Bimwright_Unicode"))))
  (vla-EndUndoMark bwuc:doc)
  (if (/= 0 (logand 8 (getvar "UNDOCTL"))) (bwuc:fail "AutoCAD did not close the owned Undo group"))
  (setq bwuc:undo-open nil)
  T)

(defun bwuc:restore-plan (plan / object caught complete)
  (setq object (bwuc:value 'object plan) complete T)
  (if (bwuc:value 'style-property plan)
    (progn
      (setq caught (vl-catch-all-apply 'vlax-put-property
                     (list object (bwuc:value 'style-property plan) (bwuc:value 'style plan))))
      (if (vl-catch-all-error-p caught) (setq complete nil))
      (setq caught (vl-catch-all-apply 'vlax-put-property
                     (list object (bwuc:value 'height-property plan) (bwuc:value 'height plan))))
      (if (vl-catch-all-error-p caught) (setq complete nil))))
  (setq caught (vl-catch-all-apply 'vla-put-Layer (list object (bwuc:value 'layer plan))))
  (if (vl-catch-all-error-p caught) (setq complete nil))
  (setq caught (vl-catch-all-apply 'vla-Update (list object)))
  (if (vl-catch-all-error-p caught) (setq complete nil))
  (setq caught (vl-catch-all-apply 'bwuc:verify-plan (list plan T)))
  (and complete (not (vl-catch-all-error-p caught)) caught))

(defun bwuc:rollback (/ plan caught complete)
  ;; Do not create additional Undo groups or restore outside the owned group.
  (if (and bwuc:writes-started (= 0 (logand 8 (getvar "UNDOCTL"))))
    nil
    (progn
  (setq complete T)
  (foreach plan bwuc:touched
    (setq caught (vl-catch-all-apply 'bwuc:restore-plan (list plan)))
    (if (or (vl-catch-all-error-p caught) (not caught)) (setq complete nil)))
  (if bwuc:new-layer
    (progn
      (setq caught (vl-catch-all-apply 'vla-Delete (list bwuc:new-layer)))
      (if (or (vl-catch-all-error-p caught) (tblsearch "LAYER" "layer-bimwright-dwg")) (setq complete nil))))
  (if bwuc:new-style
    (progn
      (setq caught (vl-catch-all-apply 'vla-Delete (list bwuc:new-style)))
      (if (or (vl-catch-all-error-p caught) (tblsearch "STYLE" "Bimwright_Unicode")) (setq complete nil))))
  ;; Detect even an uncertain native Add failure that left no returned COM object.
  (if (or (and (not bwuc:layer-existed) (tblsearch "LAYER" "layer-bimwright-dwg"))
          (and (not bwuc:style-existed) (tblsearch "STYLE" "Bimwright_Unicode")))
    (setq complete nil))
  (setq caught (vl-catch-all-apply 'vla-Regen (list bwuc:doc 1)))
  (if (vl-catch-all-error-p caught) (setq complete nil))
  (foreach plan bwuc:plans
    (setq caught (vl-catch-all-apply 'bwuc:verify-plan (list (cdr plan) T)))
    (if (or (vl-catch-all-error-p caught) (not caught)) (setq complete nil)))
  (if (or bwuc:undo-open (and bwuc:undo-attempted (/= 0 (logand 8 (getvar "UNDOCTL")))))
    (progn
      (setq caught (vl-catch-all-apply 'vla-EndUndoMark (list bwuc:doc)))
      (if (vl-catch-all-error-p caught) (setq complete nil))
      (setq bwuc:undo-open nil)))
  complete)))

(defun bw:apply-unicode (handles font_path / bwuc:doc bwuc:failure bwuc:font-path bwuc:plans
                        bwuc:touched bwuc:new-style bwuc:new-layer bwuc:undo-open bwuc:undo-attempted
                        bwuc:style-existed bwuc:layer-existed
                        bwuc:writes-started bwuc:resolved-font
                        caught requested reason restored result records entry plan reassigned)
  (vl-load-com)
  (setq bwuc:doc (vla-get-ActiveDocument (vlax-get-acad-object)))
  (setq caught (vl-catch-all-apply 'bwuc:prepare (list handles font_path)))
  (if (vl-catch-all-error-p caught)
    (setq result (list (cons 'ok nil) (cons 'phase "preflight")
                       (cons 'error (or bwuc:failure (vl-catch-all-error-message caught)))
                       (cons 'partial_changes nil) (cons 'rollback_verified T)))
    (progn
      (setq requested caught caught (vl-catch-all-apply 'bwuc:write-plans nil))
      (if (vl-catch-all-error-p caught)
        (progn
          (setq reason (or bwuc:failure (vl-catch-all-error-message caught))
                restored (bwuc:rollback))
          (setq result (list (cons 'ok nil) (cons 'phase "write") (cons 'error reason)
                             (cons 'partial_changes (not restored)) (cons 'rollback_verified restored)
                             (cons 'touched_count (length bwuc:touched))
                             (cons 'note "Property restoration covers recorded objects/resources only; inspect the drawing and Undo history before retrying"))))
        (progn
          (setq reassigned 0)
          (foreach entry bwuc:plans
            (setq plan (cdr entry))
            (if (bwuc:value 'style-property plan) (setq reassigned (1+ reassigned)))
            (if (< (length records) 10)
              (setq records
                (cons (list (cons 'handle (bwuc:value 'handle plan)) (cons 'type (bwuc:value 'kind plan))
                            (cons 'height_before (bwuc:value 'height plan))
                            (cons 'height_after (bwuc:value 'target-height plan))
                            (cons 'style_before (bwuc:value 'style plan))
                            (cons 'style_after (if (bwuc:value 'style-property plan) "Bimwright_Unicode" nil))
                            (cons 'layer_before (bwuc:value 'layer plan))
                            (cons 'layer_after (if (bwuc:value 'move-layer plan)
                                                 "layer-bimwright-dwg" (bwuc:value 'layer plan)))) records))))
          (setq result (list (cons 'ok T) (cons 'phase "completed") (cons 'partial_changes nil)
                             (cons 'requested_count requested) (cons 'reassigned_count reassigned)
                             (cons 'object_count (length bwuc:plans)) (cons 'objects_reported (length records))
                             (cons 'objects_complete (= (length bwuc:plans) (length records)))
                             (cons 'style_created (if bwuc:new-style T nil))
                             (cons 'layer_created (if bwuc:new-layer T nil))
                             (cons 'style "Bimwright_Unicode") (cons 'layer "layer-bimwright-dwg")
                             (cons 'font_path bwuc:resolved-font) (cons 'font_face "Open Sans Condensed Light")
                             (cons 'font_lookup_path bwuc:font-path) (cons 'font_downloaded nil)
                             (cons 'height_scale_mode "smart") (cons 'shx_height_scale 0.60)
                             (cons 'true_type_height_scale 0.90) (cons 'unknown_height_scale 0.82)
                             (cons 'geometry_verified T) (cons 'objects (reverse records))))))))
  (princ (strcat "\nBWUNICODE: " (vl-prin1-to-string result) "\n"))
  result)

(defun c:BWUNICODE (/ selection index handles font result)
  (vl-load-com)
  (setq selection (ssget "_I"))
  (if (not selection)
    (progn
      (prompt "\nSelect Text, MText, single-line attributes, blocks with attributes or text MLeaders: ")
      (setq selection (ssget))))
  (if selection
    (progn
      (setq index 0)
      (repeat (sslength selection)
        (setq handles (cons (cdr (assoc 5 (entget (ssname selection index)))) handles)
              index (1+ index)))
      (setq font (getfiled "Select existing OpenSans-CondensedLight.ttf (Cancel searches installed fonts)"
                           "OpenSans-CondensedLight.ttf" "ttf" 0))
      (setq result (bw:apply-unicode (reverse handles) (if font font ""))))
    (progn
      (setq result '((ok) (phase . "preflight") (error . "No objects selected") (partial_changes)))
      (princ "\nBWUNICODE: no objects selected; drawing unchanged.\n")))
  result)
