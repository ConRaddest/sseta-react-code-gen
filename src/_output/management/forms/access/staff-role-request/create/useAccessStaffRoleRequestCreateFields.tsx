import { FieldErrors } from "react-hook-form"
import { AccessStaffRoleRequestCreateRequest } from "@/types/api.types"

interface UseAccessStaffRoleRequestCreateFieldsProps {
  errors: FieldErrors<AccessStaffRoleRequestCreateRequest>
  disabledFields?: string[]
}

export default function useAccessStaffRoleRequestCreateFields(props: UseAccessStaffRoleRequestCreateFieldsProps) {
  const { errors, disabledFields = [] } = props

  return {
    requestReason: {
      props: {
        id: "staff-role-request-requestReason",
        name: "requestReason",
        heading: "Request Reason",
        type: "text" as const,
        placeholder: "Enter request reason...",
        disabled: disabledFields.includes("requestReason"),
        error: errors.requestReason,
      },
      rules: {
        required: "Please fill in this field.",
        maxLength: { value: 250, message: "Must be less than 250 characters." },
      },
    },
    name: {
      props: {
        id: "staff-role-request-name",
        name: "name",
        heading: "Name",
        type: "text" as const,
        placeholder: "Enter name...",
        disabled: disabledFields.includes("name"),
        error: errors.name,
      },
      rules: {
        maxLength: { value: 250, message: "Must be less than 250 characters." },
      },
    },
    rejectionReason: {
      props: {
        id: "staff-role-request-rejectionReason",
        name: "rejectionReason",
        heading: "Rejection Reason",
        type: "text" as const,
        placeholder: "Enter rejection reason...",
        disabled: disabledFields.includes("rejectionReason"),
        error: errors.rejectionReason,
      },
      rules: {
        maxLength: { value: 250, message: "Must be less than 250 characters." },
      },
    },
    reviewedOn: {
      props: {
        id: "staff-role-request-reviewedOn",
        name: "reviewedOn",
        heading: "Reviewed On",
        type: "datetime" as const,
        placeholder: "Select reviewed on...",
        disabled: disabledFields.includes("reviewedOn"),
        error: errors.reviewedOn,
      },
      rules: { },
    },
  }
}
