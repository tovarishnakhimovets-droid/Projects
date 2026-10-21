;; APPLOAD; command PLLENLEADERS (alias: ДЛИНЫВЫНОСОК).
;; Select LWPOLYLINEs, then existing MLEADER/LEADERs, then arrow tolerance.
;; Segment lengths are in drawing units, rounded to one decimal (no scaling).
;; Only the numeric span after one L= is replaced; empty L= is also allowed.
;; Text formatting, units, pipe/casing descriptions and geometry are preserved.
;; Every arrow must match exactly one segment, away from its vertices.
;; Multiple arrows must match the SAME segment. One Undo group; no save.
;; MLeader heads are read through ActiveX in the current representation.
;; DXF enumerates actual indexes (they need not be consecutive).
;; References: Autodesk GetLeaderLineVertices, GetBulge, getDistAtParam.
(vl-load-com)

(defun pll:fail (message)
  (setq pll:last-error message)
  (exit))

(defun pll:reason (caught / message)
  (setq message (cond (pll:last-error) ((vl-catch-all-error-message caught)))
        pll:last-error nil)
  message)

(defun pll:array (value)
  (if (= (type value) 'VARIANT) (setq value (vlax-variant-value value)))
  (if (= (type value) 'SAFEARRAY) (vlax-safearray->list value) value))

(defun pll:slice (text start count)
  (if (> count 0) (substr text (1+ start) count) ""))

(defun pll:matches (pattern text / collection result item)
  (vlax-put-property pll:rx 'Pattern pattern)
  (setq collection (vlax-invoke-method pll:rx 'Execute text))
  (vlax-for item collection
    (setq result (cons (list (vlax-get-property item 'FirstIndex)
                            (vlax-get-property item 'Length)
                            (vlax-get-property item 'Value)) result)))
  (vlax-release-object collection)
  (reverse result))

(defun pll:format (length-value separator / value)
  (setq value (vl-string-translate "," "." (rtos length-value 2 1)))
  ;; DIMZIN may suppress zeroes; restore a consistent one-decimal display.
  (if (= (substr value 1 1) ".") (setq value (strcat "0" value)))
  (if (null (vl-string-search "." value)) (setq value (strcat value ".0")))
  (if (= (substr value (strlen value) 1) ".") (setq value (strcat value "0")))
  (if (= separator ",") (vl-string-translate "." "," value) value))

(defun pll:new-text (text length-value / markers item pos previous start tail numbers span rest separator)
  (if (vl-string-search "%<" text) (pll:fail "текст содержит поле AutoCAD"))
  (foreach item (pll:matches "[LlЛл][ \t]*=[ \t]*" text)
    (setq pos (car item) previous (pll:slice text (max 0 (- pos 2)) (min 2 pos)))
    (if (or (= pos 0) (member previous '("\\P" "\\X"))
            (not (pll:matches "[A-Za-zА-Яа-яЁё0-9_]$" previous)))
      (setq markers (cons item markers))))
  (if (/= (length markers) 1) (pll:fail "нужно ровно одно обозначение L="))
  (setq item (car markers) start (+ (car item) (cadr item))
        tail (substr text (1+ start))
        numbers (pll:matches "^[0-9]+([.,][0-9]+)?" tail)
        span (if numbers (cadar numbers) 0)
        rest (substr tail (1+ span)))
  (cond
    ((and numbers (pll:matches "^([.,0-9+/-]|[eE][+-]?[0-9])" rest))
      (pll:fail "неоднозначное число после L="))
    ((not (or (= rest "")
              (pll:matches "^([ \t}]|\\\\[PX]|п[.]?[ \t]*м[.]?|м([.]|$|[ \t}\\\\]))" rest)))
      (pll:fail "после L= неподдерживаемое число, единица или форматирование")))
  (setq separator (if (and numbers (vl-string-search "." (caddar numbers))) "." ","))
  (strcat (pll:slice text 0 start) (pll:format length-value separator)
          (substr text (+ start span 1))))

(defun pll:segments (entity / data vertices item point bulge elevation normal index count first second length-value result)
  (setq data (entget entity) elevation (cond ((cdr (assoc 38 data))) (0.0))
        normal (cond ((cdr (assoc 210 data))) ('(0.0 0.0 1.0))))
  ;; Group 42 belongs to the preceding group 10 (this vertex's next segment).
  (foreach item data
    (cond
      ((= (car item) 10)
        (if point (setq vertices (cons (list point bulge) vertices)))
        (setq point (cdr item) bulge 0.0))
      ((= (car item) 42) (setq bulge (cdr item)))))
  (if point (setq vertices (reverse (cons (list point bulge) vertices))))
  (setq count (1- (length vertices)) index 0)
  (if (= 1 (logand 1 (cdr (assoc 70 data)))) (setq count (1+ count)))
  (while (< index count)
    (setq first (nth index vertices) second (nth (rem (1+ index) (length vertices)) vertices)
          length-value (- (vlax-curve-getDistAtParam entity (1+ index))
                          (vlax-curve-getDistAtParam entity index)))
    (if (and (> length-value 1e-9) (> (distance (car first) (car second)) 1e-9))
      ;; record: entity, index, length, OCS start/end, bulge, elevation, normal.
      (setq result (cons (list entity index length-value (car first) (car second)
                               (cadr first) elevation normal) result)))
    (setq index (1+ index)))
  (reverse result))

(defun pll:angle-delta (first second direction / value)
  (setq value (* direction (- second first)))
  (while (< value 0.0) (setq value (+ value (* 2.0 pi))))
  (while (>= value (* 2.0 pi)) (setq value (- value (* 2.0 pi))))
  value)

(defun pll:distance (point first second bulge / dx dy chord fraction closest offset center radius radial sweep delta)
  ;; All arguments are 2D OCS. No temporary drawing entities are created.
  (setq dx (- (car second) (car first)) dy (- (cadr second) (cadr first))
        chord (distance first second))
  (if (< (abs bulge) 1e-12)
    (progn
      (setq fraction (max 0.0 (min 1.0 (/ (+ (* (- (car point) (car first)) dx)
                                            (* (- (cadr point) (cadr first)) dy))
                                         (* chord chord))))
            closest (list (+ (car first) (* fraction dx)) (+ (cadr first) (* fraction dy))))
      (distance point closest))
    (progn
      (setq offset (/ (- 1.0 (* bulge bulge)) (* 4.0 bulge))
            center (list (- (/ (+ (car first) (car second)) 2.0) (* dy offset))
                         (+ (/ (+ (cadr first) (cadr second)) 2.0) (* dx offset)))
            radius (/ (* chord (+ 1.0 (* bulge bulge))) (* 4.0 (abs bulge)))
            radial (distance point center) sweep (* 4.0 (atan (abs bulge)))
            ;; atan uses OCS components; AutoLISP angle would depend on UCS.
            delta (pll:angle-delta (atan (- (cadr first) (cadr center)) (- (car first) (car center)))
                                  (atan (- (cadr point) (cadr center)) (- (car point) (car center)))
                                  (if (> bulge 0.0) 1.0 -1.0)))
      (if (or (< radial 1e-12) (<= delta (+ sweep 1e-10)))
        (abs (- radial radius))
        (min (distance point first) (distance point second))))))

(defun pll:match-arrow (arrow segments tolerance / segment local-point flat z-gap planar-distance candidates near-vertex)
  (foreach segment segments
    (setq local-point (trans arrow 0 (nth 7 segment))
          flat (list (car local-point) (cadr local-point))
          z-gap (- (caddr local-point) (nth 6 segment))
          planar-distance (pll:distance flat (nth 3 segment) (nth 4 segment) (nth 5 segment)))
    (if (<= (sqrt (+ (* z-gap z-gap) (* planar-distance planar-distance))) tolerance)
      (progn
        (setq candidates (cons segment candidates))
        (if (<= (sqrt (+ (* z-gap z-gap)
                        (expt (min (distance flat (nth 3 segment)) (distance flat (nth 4 segment))) 2)))
                tolerance)
          (setq near-vertex T)))))
  (cond
    (near-vertex (pll:fail "стрелка у вершины; участок неоднозначен"))
    ((null candidates) (pll:fail "стрелка вне допуска выбранных полилиний"))
    ((/= (length candidates) 1) (pll:fail "стрелке подходят несколько участков"))
    (T (car candidates))))

(defun pll:mleader-indexes (data object / item code value context node line contexts nodes lines native indexes)
  ;; Structural boundaries prevent interpreting content/style codes as indexes.
  (foreach item data
    (setq code (car item) value (cdr item))
    (cond
      ((and (= code 300) (= value "CONTEXT_DATA{"))
        (if context (pll:fail "вложенный контекст мультивыноски"))
        (setq context T contexts (1+ (cond (contexts) (0)))))
      ((and (= code 302) (= value "LEADER{"))
        (if (or (not context) node) (pll:fail "неполные данные ветви мультивыноски"))
        (setq node T))
      ((and (= code 304) (= value "LEADER_LINE{"))
        (if (or (not node) line) (pll:fail "неполные данные линии мультивыноски"))
        (setq line T))
      ((and line (= code 91)) (setq lines (cons value lines)))
      ((and node (not line) (= code 90)) (setq nodes (cons value nodes)))
      ((and (= code 305) (= value "}"))
        (if (not line) (pll:fail "несогласованные границы линии")) (setq line nil))
      ((and (= code 303) (= value "}"))
        (if (or (not node) line) (pll:fail "несогласованные границы ветви")) (setq node nil))
      ((and (= code 301) (= value "}"))
        (if (or (not context) node line) (pll:fail "несогласованные границы контекста"))
        (setq context nil))))
  (if (or context node line (/= (cond (contexts) (0)) 1) (null lines)
          (/= (length nodes) (vla-get-LeaderCount object)))
    (pll:fail "неполный или неоднозначный контекст мультивыноски"))
  ;; Compare DXF indexes with native indexes, including gaps after deletions.
  (foreach item nodes
    (setq indexes (pll:array (vla-GetLeaderLineIndexes object item))
          native (append indexes native)))
  (if (/= (length native) (length lines)) (pll:fail "неполный список стрелок"))
  (setq indexes nil)
  (foreach item lines
    (if (or (member item indexes) (not (member item native))) (pll:fail "неоднозначный индекс стрелки"))
    (setq indexes (cons item indexes)))
  lines)

(defun pll:target (entity / data object target)
  (setq data (entget entity))
  (if (= (cdr (assoc 0 data)) "MULTILEADER")
    (progn
      (setq object (vlax-ename->vla-object entity))
      (if (/= (vla-get-ContentType object) 2) (pll:fail "мультивыноска не содержит MText"))
      object)
    (progn
      (setq target (cdr (assoc 340 data)))
      (if (not (and target (member (cdr (assoc 0 (entget target))) '("MTEXT" "TEXT"))))
        (pll:fail "нет связанного TEXT/MTEXT; близость текста не используется"))
      (vlax-ename->vla-object target))))

(defun pll:leader (entity / data object target indexes coordinates heads index)
  (setq data (entget entity) object (vlax-ename->vla-object entity))
  (setq target (pll:target entity))
  (if (= (cdr (assoc 0 data)) "MULTILEADER")
    (progn
      (setq indexes (pll:mleader-indexes data object))
      (foreach index indexes
        (setq coordinates (pll:array (vla-GetLeaderLineVertices object index)))
        (if (or (< (length coordinates) 6) (/= (rem (length coordinates) 3) 0)
                (not (vl-every 'numberp coordinates)))
          (pll:fail "неполные координаты стрелки"))
        (setq heads (cons (list (car coordinates) (cadr coordinates) (caddr coordinates)) heads))))
    (progn
      (if (/= (cdr (assoc 71 data)) 1) (pll:fail "у выноски отключена стрелка"))
      (setq coordinates (pll:array (vla-get-Coordinates object)))
      (if (< (length coordinates) 6) (pll:fail "нет линии стрелки"))
      (setq heads (list (list (car coordinates) (cadr coordinates) (caddr coordinates))))))
  (list target heads))

(defun pll:writable (object / layer)
  (setq layer (vla-Item pll:layers (vla-get-Layer object)))
  (and (= (vla-get-Lock layer) :vlax-false) (vlax-write-enabled-p object)
       (vlax-property-available-p object 'TextString T)))

(defun pll:plan (entity segments tolerance / leader target text segment match key arrow)
  (setq leader (pll:leader entity) target (car leader) text (vla-get-TextString target))
  (if (not (pll:writable target)) (pll:fail "слой текста заблокирован или текст недоступен для записи"))
  (foreach arrow (cadr leader)
    (setq match (pll:match-arrow arrow segments tolerance))
    (if (and segment (not (equal (list (car segment) (cadr segment)) (list (car match) (cadr match)))))
      (pll:fail "стрелки одной выноски указывают на разные участки"))
    (setq segment match))
  (if (null segment) (pll:fail "нет стрелок"))
  (list target text (pll:new-text text (nth 2 segment)) (list (car segment) (cadr segment))
        (cdr (assoc 5 (entget entity)))))

(defun pll:issue (handle message)
  (setq pll:issues (cons (strcat handle ": " message) pll:issues)))

(defun c:PLLENLEADERS (/ *error* doc pll:layers pll:rx pll:issues pll:last-error mark polylines leaders layout tolerance
                        index entity result segments plans plan previous blocked handle target-id target updated unchanged report)
  (defun *error* (message)
    (if mark (vl-catch-all-apply 'vla-EndUndoMark (list doc)))
    (if pll:rx (vl-catch-all-apply 'vlax-release-object (list pll:rx)))
    (if pll:last-error
      (princ (strcat "\nОшибка: " pll:last-error))
      (if (not (wcmatch (strcase message) "*CANCEL*,*QUIT*,*BREAK*,*ОТМЕН*"))
        (princ (strcat "\nОшибка: " message))))
    (if mark (princ "\nСделанные изменения можно отменить одной командой U (ОТМЕНИТЬ)."))
    (princ))
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)) pll:layers (vla-get-Layers doc)
        layout (if (= (getvar "CVPORT") 1) (getvar "CTAB") "Model")
        updated 0 unchanged 0)
  (princ "\nВыбери полилинии, участки которых надо измерить: ")
  (setq polylines (ssget (list '(0 . "LWPOLYLINE") (cons 410 layout))))
  (if polylines
    (progn
      (princ "\nВыбери существующие выноски с L=: ")
      (setq leaders (ssget (list '(0 . "MULTILEADER,LEADER") (cons 410 layout))))
      (if leaders
        (progn
          (initget 6)
          (setq tolerance (getreal "\nДопуск стрелка–участок в единицах чертежа <0.1>: "))
          (if (null tolerance) (setq tolerance 0.1))
          (setq pll:rx (vlax-create-object "VBScript.RegExp"))
          (vlax-put-property pll:rx 'Global :vlax-true)
          (vlax-put-property pll:rx 'IgnoreCase :vlax-false)
          (setq index 0)
          (repeat (sslength polylines)
            (setq entity (ssname polylines index) pll:last-error nil
                  result (vl-catch-all-apply 'pll:segments (list entity)))
            (if (vl-catch-all-error-p result)
              (pll:fail (strcat "не удалось прочитать полилинию " (cdr (assoc 5 (entget entity)))
                               ": " (pll:reason result))))
            (setq segments (append result segments) index (1+ index)))
          (if (null segments) (pll:fail "в выбранных полилиниях нет ненулевых участков"))
          ;; Preflight all annotations before the first write.
          (setq index 0)
          (repeat (sslength leaders)
            (setq entity (ssname leaders index) handle (cdr (assoc 5 (entget entity)))
                  pll:last-error nil target (vl-catch-all-apply 'pll:target (list entity)))
            (if (vl-catch-all-error-p target)
              (setq result target target-id nil)
              (setq target-id (vla-get-Handle target)
                    result (vl-catch-all-apply 'pll:plan (list entity segments tolerance))))
            (if (vl-catch-all-error-p result)
              (progn
                (pll:issue handle (pll:reason result))
                ;; A failed selected LEADER also blocks its shared annotation.
                (if target-id
                  (progn
                    (setq blocked (cons target-id blocked))
                    (foreach plan plans
                      (if (= (vla-get-Handle (car plan)) target-id)
                        (pll:issue (nth 4 plan) "общий текст связан с выноской, не прошедшей проверку")))
                    (setq plans (vl-remove-if '(lambda (item) (= (vla-get-Handle (car item)) target-id)) plans)))))
              (progn
                (setq previous (vl-some '(lambda (item) (if (= (vla-get-Handle (car item)) target-id) item)) plans))
                (cond
                  ((member (vla-get-Handle (car result)) blocked)
                    (pll:issue handle "общий текст уже исключён из-за разных участков"))
                  ((and previous (not (equal (nth 3 previous) (nth 3 result))))
                    (setq plans (vl-remove previous plans)
                          blocked (cons (vla-get-Handle (car result)) blocked))
                    (pll:issue (nth 4 previous) "общий текст связан с разными участками")
                    (pll:issue handle "общий текст связан с разными участками"))
                  ((null previous) (setq plans (cons result plans))))))
            (setq index (1+ index)))
          (if plans
            (progn
              (vla-StartUndoMark doc) (setq mark T)
              (foreach plan (reverse plans)
                (cond
                  ((not (and (pll:writable (car plan)) (= (vla-get-TextString (car plan)) (cadr plan))))
                    (pll:issue (nth 4 plan) "текст/доступ изменился после проверки"))
                  ((= (cadr plan) (caddr plan)) (setq unchanged (1+ unchanged)))
                  (T
                    (setq result (vl-catch-all-apply 'vla-put-TextString (list (car plan) (caddr plan))))
                    (if (or (vl-catch-all-error-p result) (/= (vla-get-TextString (car plan)) (caddr plan)))
                      (progn
                        (pll:issue (nth 4 plan) "запись не подтверждена; проверь текст или отмени команду"))
                      (setq updated (1+ updated))))))
              (vla-EndUndoMark doc) (setq mark nil)))
          (setq report (strcat "Обновлено: " (itoa updated) "; уже верно: " (itoa unchanged)
                               "; замечаний: " (itoa (length pll:issues)) "."))
          (foreach result (reverse pll:issues) (setq report (strcat report "\n" result)))
          (setq *pllenleader-report* report)
          (princ (strcat "\n" report))
          (vlax-release-object pll:rx) (setq pll:rx nil)))))
  (princ))

(defun c:ДЛИНЫВЫНОСОК () (c:PLLENLEADERS))
(princ "\nЗагружено: PLLENLEADERS / ДЛИНЫВЫНОСОК — длины участков в существующие выноски.")
(princ)
